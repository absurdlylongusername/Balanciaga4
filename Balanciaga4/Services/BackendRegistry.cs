using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Services;

public sealed class BackendRegistry : IBackendRegistry
{
    private ILogger<BackendRegistry> Logger { get; }
    private IOptionsMonitor<LbOptions> OptionsMonitor { get; }

    private ConcurrentDictionary<IPEndPoint, BackendInfo> BackendMap { get; } = new();
    private ImmutableArray<IPEndPoint> OrderedEndpoints { get; set; } = ImmutableArray<IPEndPoint>.Empty;


    public BackendRegistry(ILogger<BackendRegistry> logger, IOptionsMonitor<LbOptions> optionsMonitorMonitor)
    {
        Logger = logger;
        OptionsMonitor = optionsMonitorMonitor;

        LoadOptions(optionsMonitorMonitor.CurrentValue, "Initial load");
        optionsMonitorMonitor.OnChange(m => LoadOptions(m, "reload"));
    }

    private void LoadOptions(LbOptions options, string reason)
    {
        var configuredSet = new HashSet<IPEndPoint>(options.BackendEndpoints);
        var existingEndpoints = BackendMap.Keys.ToList();
        OrderedEndpoints = [..options.BackendEndpoints];

        // Add new endpoints
        foreach (var endpoint in configuredSet.Except(existingEndpoints))
        {
            var info = new BackendInfo
            {
                Endpoint = endpoint,
                Health = BackendHealth.Unknown, // eligible until first probe
                LastChangeUtc = DateTimeOffset.UtcNow,
                LastReason = $"configured ({reason})",
                ActiveConnections = 0
            };

            BackendMap.TryAdd(endpoint, info);
            Logger.LogInformation("Backend added: {Endpoint}", endpoint);
        }

        // Remove deconfigured
        foreach (var endpoint in existingEndpoints.Except(configuredSet))
        {
            if (BackendMap.TryRemove(endpoint, out _))
            {
                Logger.LogInformation("Backend removed: {Endpoint}", endpoint);
            }
        }

        foreach (var endpoint in configuredSet.Intersect(existingEndpoints))
        {
            if (BackendMap.TryGetValue(endpoint, out var info))
            {
                info.LastReason = $"configured ({reason})";
            }
        }
    }

    public IReadOnlyList<IPEndPoint> GetAllEndpoints() => OrderedEndpoints;

    public IReadOnlyList<IPEndPoint> GetHealthyEndpoints()
    {
        // Treat Unknown as Up until first probe, to avoid black-holing on startup.

        List<IPEndPoint> result = [];
        foreach (var endpoint in OrderedEndpoints)
        {
            if (!BackendMap.TryGetValue(endpoint, out var info)) continue;

            if (info.Health == BackendHealth.Up || info.Health == BackendHealth.Unknown)
            {
                result.Add(endpoint);
            }
        }

        return result;
    }

    public BackendInfo GetInfo(IPEndPoint endpoint)
    {
        if (!BackendMap.TryGetValue(endpoint, out var info))
        {
            throw new KeyNotFoundException($"Backend {endpoint} not registered.");
        }
        return info;
    }

    public void MarkAsUp(IPEndPoint endpoint, string reason, DateTimeOffset nowUtc)
    {
        if (!BackendMap.TryGetValue(endpoint, out var info)) return;

        if (info.Health == BackendHealth.Up) return;
        info.Health = BackendHealth.Up;
        info.LastChangeUtc = nowUtc;
        info.LastReason = reason;
        Logger.LogInformation("Server {EndPoint} -> UP ({Reason})", endpoint, reason);
    }

    public void MarkAsDown(IPEndPoint endpoint, string reason, DateTimeOffset nowUtc)
    {
        if (!BackendMap.TryGetValue(endpoint, out var info)) return;

        if (info.Health == BackendHealth.Down) return;
        info.Health = BackendHealth.Down;
        info.LastChangeUtc = nowUtc;
        info.LastReason = reason;
        Logger.LogInformation("Server {EndPoint} -> DOWN ({Reason})", endpoint, reason);
    }


    public void IncrementConnection(IPEndPoint endpoint)
    {
        if (!BackendMap.TryGetValue(endpoint, out var info)) return;
        var valueActiveConnections = info.ActiveConnections;
        Interlocked.Increment(ref valueActiveConnections);
    }

    public void DecrementConnection(IPEndPoint endpoint)
    {
        if (!BackendMap.TryGetValue(endpoint, out var info)) return;
        var valueActiveConnections = info.ActiveConnections;
        Interlocked.Decrement(ref valueActiveConnections);
    }
}
