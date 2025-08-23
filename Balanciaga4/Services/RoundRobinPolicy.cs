using System.Net;
using Balanciaga4.Interfaces;

namespace Balanciaga4.Services;

public sealed class RoundRobinPolicy : ILoadBalancingPolicy
{
    private int _idx = -1;

    public IPEndPoint? Choose(IReadOnlyList<IPEndPoint> healthy, ConnectionContext context)
    {
        if (healthy.Count == 0) return null;
        var i = Math.Abs(Interlocked.Increment(ref _idx)) % healthy.Count;
        return healthy[i];
    }
}
