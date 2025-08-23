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
    private readonly ILogger<TcpListenerService> _logger;
    private readonly IOptionsMonitor<LbOptions> _opts;
    private readonly IConnectionDispatcher _dispatcher;

    public TcpListenerService(ILogger<TcpListenerService> log, IOptionsMonitor<LbOptions> opts, IConnectionDispatcher dispatcher)          
    {
        _logger = log; _opts = opts; _dispatcher = dispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listenEndpoint = _opts.CurrentValue.ListenEndpoint;
        var listener = new TcpListener(listenEndpoint);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start(_opts.CurrentValue.Limits.Backlog);

        _logger.LogInformation("Listening on {Listen} (backlog={Backlog})", listenEndpoint,
                               _opts.CurrentValue.Limits.Backlog);

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

                _ = Task.Run(() => _dispatcher.DispatchAsync(client, stoppingToken), stoppingToken)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted) _logger.LogError(t.Exception, "Dispatch error");
                        }, stoppingToken);
            }
        }
        finally
        {
            listener.Stop();
            _logger.LogInformation("Listener stopped");
        }
    }
}
