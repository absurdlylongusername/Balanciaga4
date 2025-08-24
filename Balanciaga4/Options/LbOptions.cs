using System.Net;

namespace Balanciaga4.Options;

public sealed class LbOptions
{
    public IPEndPoint ListenEndpoint { get; set; } = new(IPAddress.Any, 8080);
    public bool UpgradeListenToDualStack { get; set; } = true;
    public Policy Policy { get; set; } = Policy.RoundRobin;
    public IReadOnlyList<IPEndPoint> BackendEndpoints { get; set; } = [];
    public LimitsOptions Limits { get; set; } = new();
    public TimeoutsOptions Timeouts { get; set; } = new();
    public HealthCheckOptions HealthCheck { get; set; } = new();
}

public enum Policy
{
    RoundRobin,
    Random,
    LeastConnections,
    SourceIpHash,
    ConsistentHash
}

public sealed class LimitsOptions
{
    public int MaxConnections { get; set; } = 10000;

    /// <summary>
    /// Maximum number of pending connections
    /// </summary>
    public int Backlog { get; set; } = 512;
}

public sealed class TimeoutsOptions
{
    public int ConnectMs { get; set; } = 2000;
    public int IdleMs { get; set; } = 60000;
}

public sealed class HealthCheckOptions
{
    public int IntervalMs { get; set; } = 2000;
    public int ConnectTimeoutMs { get; set; } = 500;
}
