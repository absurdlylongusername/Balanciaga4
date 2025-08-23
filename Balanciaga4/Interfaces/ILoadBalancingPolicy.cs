using System.Net;

namespace Balanciaga4.Interfaces;

public interface ILoadBalancingPolicy
{
    IPEndPoint? Choose(IReadOnlyList<IPEndPoint> healthy, ConnectionContext context);
}

public sealed record ConnectionContext(string ClientIp, int ClientPort);
