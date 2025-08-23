using System.Collections.Concurrent;
using System.Net;
using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Services;

public sealed class BackendRegistry : IBackendRegistry
{
    private ConcurrentDictionary<IPEndPoint, int> ActiveConnections { get; } = new();
    private volatile IReadOnlyList<IPEndPoint> _healthyEndpoints;

    public BackendRegistry(IOptionsMonitor<LbOptions> opts)
    {
        void Reload(LbOptions options)
        {
            var endpoints = options.BackendEndpoints;
            _healthyEndpoints = endpoints;
            foreach (var endpoint in endpoints) ActiveConnections.TryAdd(endpoint, 0);
        }

        Reload(opts.CurrentValue);
        opts.OnChange(Reload);
    }

    public IReadOnlyList<IPEndPoint> GetHealthyEndpoints() => _healthyEndpoints;

    public int IncrementConnection(IPEndPoint backendEndpoint) =>
        ActiveConnections.AddOrUpdate(backendEndpoint, 1, (_, v) => v + 1);

    public int DecrementConnection(IPEndPoint backendEndpoint) =>
        ActiveConnections.AddOrUpdate(backendEndpoint, 0, (_, v) => Math.Max(0, v - 1));
}
