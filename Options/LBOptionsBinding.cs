using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Options;

public sealed class LbOptionsConfigurator : IConfigureOptions<LbOptions>
{
    private readonly IOptionsMonitor<LbOptionsRaw> _rawMonitor;

    public LbOptionsConfigurator(IOptionsMonitor<LbOptionsRaw> rawMonitor)
    {
        _rawMonitor = rawMonitor;
    }

    public void Configure(LbOptions options)
    {
        var raw = _rawMonitor.CurrentValue;

        options.Policy = raw.Policy;
        options.Limits = raw.Limits;
        options.Timeouts = raw.Timeouts;
        options.HealthCheck = raw.HealthCheck;

        options.ListenEndpoint = ParseEndPoint(raw.Listen);

        var endpoints = new List<IPEndPoint>(raw.Backends.Length);
        foreach (var backend in raw.Backends)
        {
            endpoints.Add(ParseEndPoint(backend));
        }
        options.BackendEndpoints = endpoints;
    }

    private static IPEndPoint ParseEndPoint(string value)
    {
        if (IPEndPoint.TryParse(value, out var endpoint))
        {
            return endpoint;
        }

        // Allow "host:port" (DNS name) via Dns.GetHostAddresses if value isn’t a literal IP.
        // Still no manual colon-splitting; we rely on Parse/TryParse for literal IPs, otherwise use URI parser as a helper.
        if (Uri.TryCreate($"tcp://{value}", UriKind.Absolute, out var uri) && uri.Port > 0 && !string.IsNullOrWhiteSpace(uri.Host))
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var ip = ipv4 ?? addresses.First(); // prefer IPv4 when present
            return new IPEndPoint(ip, uri.Port);
        }

        throw new OptionsValidationException(nameof(LbOptions), typeof(LbOptions), [$"Invalid endpoint: '{value}'"]);
    }
}

public sealed class LbOptionsValidator : IValidateOptions<LbOptions>
{
    public ValidateOptionsResult Validate(string? name, LbOptions options)
    {
        var errors = new List<string>();

        if (options.ListenEndpoint is null)
        {
            errors.Add("ListenEndpoint must be specified and valid.");
        }

        if (options.BackendEndpoints is null || options.BackendEndpoints.Count == 0)
        {
            errors.Add("At least one backend endpoint must be configured.");
        }

        if (options.Limits is null || options.Limits.MaxConnections <= 0)
        {
            errors.Add("Limits.MaxConnections must be greater than 0.");
        }

        if (options.Timeouts is null || options.Timeouts.ConnectMs <= 0 || options.Timeouts.IdleMs <= 0)
        {
            errors.Add("Timeouts.ConnectMs and Timeouts.IdleMs must be greater than 0.");
        }

        if (errors.Count > 0)
        {
            return ValidateOptionsResult.Fail(errors);
        }

        return ValidateOptionsResult.Success;
    }
}
