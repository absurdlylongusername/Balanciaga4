using System.Net;

namespace Balanciaga4.Interfaces;

public enum BackendHealth
{
    Unknown = 0,
    Up = 1,
    Down = 2
}

public sealed class BackendInfo
{
    public required IPEndPoint Endpoint { get; init; }
    public BackendHealth Health { get; set; } = BackendHealth.Unknown;
    public DateTimeOffset LastChangeUtc { get; set; } = DateTimeOffset.UtcNow;
    public int ActiveConnections { get; set; } = 0;
    public string? LastReason { get; set; }
}
