using System.Net.Sockets;
using System.Net;
using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Services;

public sealed class ProxySession : IProxySession
{
    private ILogger<ProxySession> Logger { get; }
    private IOptionsMonitor<LbOptions> OptionsMonitor { get; }
    private IBytePump BytePump { get; }
    private IBackendRegistry BackendRegistry { get; }

    public ProxySession(ILogger<ProxySession> logger,
                        IOptionsMonitor<LbOptions> optionsMonitor,
                        IBytePump bytePump,
                        IBackendRegistry backendRegistry)
    {
        Logger = logger;
        OptionsMonitor = optionsMonitor;
        BytePump = bytePump;
        BackendRegistry = backendRegistry;
    }

    public async Task RunAsync(TcpClient clientTcpClient, IPEndPoint backendEndPoint, CancellationToken cancellationToken)
    {
        using var backendTcpClient = new TcpClient();
        var options = OptionsMonitor.CurrentValue;

        using var connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeoutCts.CancelAfter(options.Timeouts.ConnectMs);

        try
        {
            await backendTcpClient.ConnectAsync(backendEndPoint, connectTimeoutCts.Token);
            Logger.LogDebug("Connected to backend server {BackendEndPoint}", backendEndPoint);
        }
        catch (OperationCanceledException exception)
        {
            Logger.LogWarning(exception, "Backend connect timeout to {Backend}", backendEndPoint);
            return;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Backend connect failed to {Backend}", backendEndPoint);
            return;
        }

        BackendRegistry.IncrementConnection(backendEndPoint);

        try
        {
            backendTcpClient.NoDelay = true;

            // Idle timeout for the entire session: cancel both pumps if no activity for IdleMs.
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Log when idle timer elapses
            var clientRemoteEndPoint = clientTcpClient.Client.RemoteEndPoint as IPEndPoint;
            idleCts.Token.Register(() =>
            {
                Logger.LogInformation("Idle timeout elapsed for session {Client}->{Backend}.",
                                      clientRemoteEndPoint,
                                      backendEndPoint);
            });

            void ResetIdle() => idleCts.CancelAfter(options.Timeouts.IdleMs);

            await using var clientStream = new NetworkStream(clientTcpClient.Client, ownsSocket: false);
            await using var backendStream = new NetworkStream(backendTcpClient.Client, ownsSocket: false);

            await using var clientObserved =
                new NotifyingStream(clientStream,
                                    onRead: bytes =>
                                    {
                                        ResetIdle();
                                        Logger.LogDebug("{Client}->{Server} read {Count} bytes from client to LB",
                                                        clientRemoteEndPoint,
                                                        backendEndPoint, bytes);
                                    },
                                    onWrite: bytes =>
                                    {
                                        ResetIdle();
                                        Logger.LogDebug("{Client}->{Server} sent {Count} bytes from LB to client",
                                                        clientRemoteEndPoint,
                                                        backendEndPoint, bytes);
                                    });

            await using var backendObserved =
                new NotifyingStream(backendStream,
                                    onRead: bytes =>
                                    {
                                        ResetIdle();
                                        Logger.LogDebug("{Server}->{Client} read {Count} bytes from server to LB",
                                                        backendEndPoint,
                                                        clientRemoteEndPoint, bytes);
                                    },
                                    onWrite: bytes =>
                                    {
                                        ResetIdle();
                                        Logger.LogDebug("{Server}->{Client} sent {Count} bytes to server to LB",
                                                        backendEndPoint,
                                                        clientRemoteEndPoint, bytes);
                                    });

            Logger.LogDebug("Setting up pumps");
            var clientToBackend = BytePump.PipeAsync(clientObserved, backendObserved, idleCts.Token);
            var backendToClient = BytePump.PipeAsync(backendObserved, clientObserved, idleCts.Token);
            Logger.LogDebug("Pumps set up");
            ResetIdle(); //Starts idle timer after data transfer has begun

            var firstCompleted = await Task.WhenAny(clientToBackend, backendToClient);
            var (first, second) =
                firstCompleted == clientToBackend ?
                    ((clientRemoteEndPoint, clientToBackend), (backendEndPoint , backendToClient))
                  : ((backendEndPoint, clientToBackend), (clientRemoteEndPoint , backendToClient));

            Logger.LogDebug("{Endpoint} completed first", first.Item1);

            TryShutdownSend(backendTcpClient);
            await Task.WhenAny(second.Item2);
            Logger.LogDebug("{Endpoint} completed second", second.Item1);
        }
        finally
        {
            BackendRegistry.DecrementConnection(backendEndPoint);
        }
    }

    private static void TryShutdownSend(TcpClient tcpClient)
    {
        try
        {
            tcpClient.Client.Shutdown(SocketShutdown.Send);
        }
        catch
        {
            // ignore
        }
    }
}
