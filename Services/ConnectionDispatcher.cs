using System.Net.Sockets;
using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Services;

public sealed class ConnectionDispatcher : IConnectionDispatcher
{
    private readonly ILogger<ConnectionDispatcher> _logger;
    private readonly IOptionsMonitor<LbOptions> _optionsMonitor;
    private readonly IBackendRegistry _backendRegistry;
    private readonly ILoadBalancingPolicy _loadBalancingPolicy;

    public ConnectionDispatcher(ILogger<ConnectionDispatcher> log, 
                                IOptionsMonitor<LbOptions> optionsMonitor,
                                IBackendRegistry backendRegistry,
                                ILoadBalancingPolicy loadBalancingPolicy)
    {
        _logger = log;
        _optionsMonitor = optionsMonitor;
        _backendRegistry = backendRegistry;
        _loadBalancingPolicy = loadBalancingPolicy;
    }

    public async Task DispatchAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            _logger.LogDebug("Accepted {Client}", clientEndpoint);

            // For now, we have no proxy session; just close politely.
            try
            {
                client.Client.Shutdown(SocketShutdown.Both);
            }
            catch
            {

            }
            await Task.CompletedTask;
        }
    }
}
