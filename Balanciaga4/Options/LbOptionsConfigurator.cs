using System.Net;
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
        Logger.LogDebug("idle timeout is {Timeout}", options.Timeouts.IdleMs);
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

        var upgradeListenToDualStack = LoadBalancerSection[nameof(LbOptions.UpgradeListenToDualStack)];
        if (string.IsNullOrWhiteSpace(upgradeListenToDualStack))
        {
            Logger.LogWarning("LoadBalancer:UpgradeListenToDualStack not specified. Using default value {Default}",
                              options.UpgradeListenToDualStack);
        }
        else
        {
            options.UpgradeListenToDualStack = bool.Parse(upgradeListenToDualStack);
        }


        options.ListenEndpoint = NormalizeListenForDualStack(ParseEndPointOrThrow(listenValue), options.UpgradeListenToDualStack);

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

    private static IPEndPoint NormalizeListenForDualStack(IPEndPoint ep, bool upgrade)
    {
        if (!upgrade) return ep;

        // Only upgrade “wildcards” and loopback; never touch specific IPs.
        if (ep.Address.Equals(IPAddress.Any) ||
            ep.Address.Equals(IPAddress.IPv6Any))
        {
            return new IPEndPoint(IPAddress.IPv6Any, ep.Port);
        }

        if (ep.Address.Equals(IPAddress.Loopback) ||
            ep.Address.Equals(IPAddress.IPv6Loopback))
        {
            return new IPEndPoint(IPAddress.IPv6Loopback, ep.Port);
        }

        // If someone typed “localhost” in config → treat as loopback
        if (ep.Address.IsIPv4MappedToIPv6 && ep.Address.MapToIPv4().Equals(IPAddress.Loopback))
            return new IPEndPoint(IPAddress.IPv6Loopback, ep.Port);

        return ep;
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
