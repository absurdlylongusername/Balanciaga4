using System.Net;
using System.Net.Sockets;
using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Services;

public sealed class ConnectionDispatcher : IConnectionDispatcher
{
    private ILogger<ConnectionDispatcher> Logger { get; }
    private IOptionsMonitor<LbOptions> OptionsMonitor { get; }
    private IBackendRegistry BackendRegistry { get; }
    private ILoadBalancingPolicy LoadBalancingPolicy { get; }
    private IProxySession ProxySession { get; }

    public ConnectionDispatcher(ILogger<ConnectionDispatcher> logger,
                                IOptionsMonitor<LbOptions> optionsMonitor,
                                IBackendRegistry backendRegistry,
                                ILoadBalancingPolicy loadBalancingPolicy,
                                IProxySession proxySession)
    {
        Logger = logger;
        OptionsMonitor = optionsMonitor;
        BackendRegistry = backendRegistry;
        LoadBalancingPolicy = loadBalancingPolicy;
        ProxySession = proxySession;
    }

    public async Task DispatchAsync(TcpClient clientTcpClient, CancellationToken cancellationToken)
    {
        var remote = clientTcpClient.Client.RemoteEndPoint as IPEndPoint ?? throw new InvalidOperationException();
        var clientContext = new ConnectionContext(remote.Address.ToString(), remote.Port);

        var healthyEndpoints = BackendRegistry.GetHealthyEndpoints();
        var chosenBackend = LoadBalancingPolicy.Choose(healthyEndpoints, clientContext);

        if (chosenBackend is null)
        {
            Logger.LogWarning("No healthy backends; dropping connection from {Remote}", remote);
            TryCloseNow(clientTcpClient);
            return;
        }

        Logger.LogDebug("Routing {Remote} -> {Backend}", remote, chosenBackend);

        await ProxySession.RunAsync(clientTcpClient, chosenBackend, cancellationToken);
    }

    private static void TryCloseNow(TcpClient tcpClient)
    {
        try
        {
            tcpClient.Client.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // ignore
        }

        try
        {
            tcpClient.Close();
        }
        catch
        {
            // ignore
        }
    }
}
