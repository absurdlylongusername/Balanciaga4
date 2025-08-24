using System.Net;
using System.Net.Sockets;
using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Services;

public sealed class TcpListenerService : BackgroundService
{
    private ILogger<TcpListenerService> Logger { get; }
    private IOptionsMonitor<LbOptions> OptionsMonitor { get; }
    private IConnectionDispatcher ConnectionDispatcher { get; }

    public TcpListenerService(ILogger<TcpListenerService> logger, IOptionsMonitor<LbOptions> optionsMonitor, IConnectionDispatcher connectionDispatcher)
    {
        Logger = logger;
        OptionsMonitor = optionsMonitor;
        ConnectionDispatcher = connectionDispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listenEndpoint = OptionsMonitor.CurrentValue.ListenEndpoint;
        var listener = new TcpListener(listenEndpoint);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start(OptionsMonitor.CurrentValue.Limits.Backlog);

        Logger.LogInformation("Listening on {Listen} (backlog={Backlog})", listenEndpoint,
                               OptionsMonitor.CurrentValue.Limits.Backlog);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }

                var endpoint = (client.Client.RemoteEndPoint as IPEndPoint)!;
                Logger.LogInformation("Accepted client {Client}:{Port}", endpoint.Address.ToString(), endpoint.Port);
                client.NoDelay = true;
                _ = Task.Run(() => ConnectionDispatcher.DispatchAsync(client, stoppingToken), stoppingToken)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                Logger.LogError(t.Exception,
                                                "Dispatch error for client {Address}:{Port}",
                                                endpoint.Address,
                                                endpoint.Port);
                            }
                            Logger.LogDebug("Disposing of {Endpoint}", endpoint);
                            client.Dispose();
                            Logger.LogDebug("Disposed of {Endpoint}", endpoint);
                        }, stoppingToken);
            }
        }
        finally
        {
            listener.Stop();
            Logger.LogInformation("Listener stopped");
        }
    }
}
