using System.Net;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Options;

public sealed class LbOptionsValidator : IValidateOptions<LbOptions>
{
    public ValidateOptionsResult Validate(string? name, LbOptions options)
    {
        var failures = new List<string>();

        // Logical validation done here: parsing validation done in configurator
        if (options.Timeouts.ConnectMs <= 0)
        {
            failures.Add("Timeouts.ConnectMs must be greater than 0.");
        }

        if (options.Timeouts.IdleMs <= 0)
        {
            failures.Add("Timeouts.IdleMs must be greater than 0.");
        }

        if (options.Limits.MaxConnections <= 0)
        {
            failures.Add("Limits.MaxConnections must be greater than 0.");
        }

        // No duplicates (same IP+port)
        var distinctCount = options.BackendEndpoints.Distinct(new IPEndPointComparer()).Count();
        if (distinctCount != options.BackendEndpoints.Count)
        {
            failures.Add("Backends contain duplicate endpoints.");
        }

        // Avoid hairpin: listen endpoint must not be in backend pool
        if (options.BackendEndpoints.Any(ep => AreSameEndpoint(ep, options.ListenEndpoint)))
        {
            failures.Add("Listen endpoint must not appear in Backends.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    private static bool AreSameEndpoint(IPEndPoint a, IPEndPoint b)
    {
        return a.Address.Equals(b.Address) && a.Port == b.Port;
    }

    private sealed class IPEndPointComparer : IEqualityComparer<IPEndPoint>
    {
        public bool Equals(IPEndPoint? x, IPEndPoint? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return AreSameEndpoint(x, y);
        }

        public int GetHashCode(IPEndPoint obj)
        {
            return HashCode.Combine(obj.Address, obj.Port);
        }
    }
}
