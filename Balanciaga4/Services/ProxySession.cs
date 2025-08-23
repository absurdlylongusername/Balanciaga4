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
            idleCts.CancelAfter(options.Timeouts.IdleMs);

            // Log when idle timer elapses
            idleCts.Token.Register(() =>
            {
                Logger.LogInformation("Idle timeout elapsed for session {Client}->{Backend}.",
                                clientTcpClient.Client.RemoteEndPoint,
                                backendEndPoint);
            });

            await using var clientObserved = new NotifyingStream(clientTcpClient.GetStream(), ResetIdle);
            await using var backendObserved = new NotifyingStream(backendTcpClient.GetStream(), ResetIdle);

            // Two directional pumps; either side closing should half-close the opposite and allow a short drain.
            var clientToBackend = BytePump.PipeAsync(clientObserved, backendObserved, idleCts.Token);
            var backendToClient = BytePump.PipeAsync(backendObserved, clientObserved, idleCts.Token);

            var firstCompleted = await Task.WhenAny(clientToBackend, backendToClient);
            var other = firstCompleted == clientToBackend ? backendToClient : clientToBackend;

            // Attempt half-close in both directions to signal EOF while allowing the other side to drain briefly.
            TryShutdownSend(clientTcpClient);
            TryShutdownSend(backendTcpClient);

            // Let the other direction drain briefly or until completion.
            await Task.WhenAny(other, Task.Delay(500, default));

            void ResetIdle()
            {
                idleCts.CancelAfter(options.Timeouts.IdleMs);
            }
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
