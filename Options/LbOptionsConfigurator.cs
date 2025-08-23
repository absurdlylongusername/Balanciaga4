using System.Net;
using Balanciaga4.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Options;

public sealed class LbOptionsConfigurator : IConfigureOptions<LbOptions>
{
    private IConfigurationSection LoadBalancerSection { get; }
    private ILogger<LbOptionsConfigurator> Logger { get; }


    public LbOptionsConfigurator(ILogger<LbOptionsConfigurator> logger, IConfigurationSection loadBalancerSection)
    {
        LoadBalancerSection = loadBalancerSection;
        Logger = logger;
    }

    public void Configure(LbOptions options)
    {
        Logger.LogInformation("Configuring LbOptions");
        // Bind sub-objects with primitives directly from configuration
        var limits = new LimitsOptions();
        LoadBalancerSection.GetSection(nameof(LbOptions.Limits)).Bind(limits);

        var timeouts = new TimeoutsOptions();
        LoadBalancerSection.GetSection(nameof(LbOptions.Timeouts)).Bind(timeouts);

        var health = new HealthCheckOptions();
        LoadBalancerSection.GetSection(nameof(LbOptions.HealthCheck)).Bind(health);

        options.Limits = limits;
        options.Timeouts = timeouts;
        options.HealthCheck = health;

        // Policy (enum)
        var policyString = LoadBalancerSection[nameof(LbOptions.HealthCheck)];
        if (!string.IsNullOrWhiteSpace(policyString) &&
            Enum.TryParse<Policy>(policyString, ignoreCase: true, out var parsedPolicy))
        {
            options.Policy = parsedPolicy;
        }

        Logger.LogInformation("Load balancing policy: {Policy}", options.Policy);

        // Listen endpoint
        var listenValue = LoadBalancerSection[nameof(LbOptions.ListenEndpoint)];
        if (string.IsNullOrWhiteSpace(listenValue))
        {
            throw new OptionsValidationException(
                nameof(LbOptions),
                typeof(LbOptions),
                ["LoadBalancer:Listen must be specified."]);
        }

        options.ListenEndpoint = ParseEndPointOrThrow(listenValue);

        // Backend endpoints
        var backendStrings = LoadBalancerSection.GetSection(nameof(LbOptions.BackendEndpoints)).Get<string[]>() ?? [];
        if (backendStrings.Length == 0)
        {
            throw new OptionsValidationException(
                nameof(LbOptions),
                typeof(LbOptions),
                ["LoadBalancer:Backends must contain at least one endpoint."]);
        }

        var parsedBackends = new List<IPEndPoint>(backendStrings.Length);
        foreach (var backend in backendStrings)
        {
            if (string.IsNullOrWhiteSpace(backend))
            {
                throw new OptionsValidationException(
                    nameof(LbOptions),
                    typeof(LbOptions),
                    ["LoadBalancer:Backends contains an empty value."]);
            }

            parsedBackends.Add(ParseEndPointOrThrow(backend));
        }

        options.BackendEndpoints = parsedBackends;
    }

    private static IPEndPoint ParseEndPointOrThrow(string value)
    {
        // Prefer framework parser first (handles IP-literals w/port, IPv6-in-brackets, etc.)
        if (IPEndPoint.TryParse(value, out var endPoint))
        {
            return endPoint;
        }

        // If it wasn't a literal IP, allow hostnames via URI + DNS resolution (no manual string splitting).
        if (!Uri.TryCreate($"tcp://{value}", UriKind.Absolute, out var uri) ||
            uri.Port <= 0 ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new OptionsValidationException(
                nameof(LbOptions),
                typeof(LbOptions),
                [$"Invalid endpoint format: '{value}'. Expected 'IP:port' or 'host:port'."]);
        }

        var addresses = Dns.GetHostAddresses(uri.Host);
        if (addresses.Length == 0)
        {
            throw new OptionsValidationException(
                nameof(LbOptions),
                typeof(LbOptions),
                [$"Unable to resolve hostname '{uri.Host}'."]);
        }

        // Prefer IPv4 when available to avoid surprises on dual-stack hosts
        var preferred = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                     ?? addresses[0];

        return new IPEndPoint(preferred, uri.Port);

    }
}
