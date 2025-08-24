using System.Net;
using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Balanciaga4.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Balanciaga4.IntegrationTests.Helpers;

public sealed class LoadBalancerHost : IAsyncDisposable
{
    public int ListenPort { get; }
    private readonly IHost? _host;

    public LoadBalancerHost(int listenPort, IEnumerable<IPEndPoint> backendEndpoints, int idleMs = 50)
    {
        ListenPort = listenPort;

        var inMemory = new Dictionary<string, string?>
        {
            [$"LoadBalancer:{nameof(LbOptions.ListenEndpoint)}"] = $"127.0.0.1:{listenPort}",
            [$"LoadBalancer:{nameof(LbOptions.Policy)}"] = nameof(Policy.RoundRobin),
            [$"LoadBalancer:{nameof(LbOptions.Timeouts)}:{nameof(TimeoutsOptions.ConnectMs)}"] = "50",
            [$"LoadBalancer:{nameof(LbOptions.Timeouts)}:{nameof(TimeoutsOptions.IdleMs)}"] = idleMs.ToString(),
            [$"LoadBalancer:{nameof(LbOptions.Limits)}:{nameof(LimitsOptions.MaxConnections)}"] = "10000",
            [$"LoadBalancer:{nameof(LbOptions.Limits)}:{nameof(LimitsOptions.Backlog)}"] = "512",
            [$"LoadBalancer:{nameof(LbOptions.HealthCheck)}:{nameof(HealthCheckOptions.JitterMs)}"] = "0",
        };

        var index = 0;
        foreach (var endpoints in backendEndpoints)
        {
            inMemory[$"LoadBalancer:{nameof(LbOptions.BackendEndpoints)}:{index}"] = $"{endpoints.Address}:{endpoints.Port}";
            index++;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();

        var builder = Host.CreateApplicationBuilder();

        // Logging to console for visibility in test output
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "[HH:mm:ss.fff] ";
            options.IncludeScopes = true;
            options.SingleLine = true;
        });
        builder.Logging.SetMinimumLevel(LogLevel.Trace);

        // === Reproduce Program.cs DI wiring ===
        var lbSection = config.GetSection("LoadBalancer");

        builder.Services.AddOptions<LbOptions>().ValidateOnStart();

        builder.Services.AddSingleton<IConfigureOptions<LbOptions>>(sp =>
            new LbOptionsConfigurator(sp.GetRequiredService<ILogger<LbOptionsConfigurator>>(), lbSection));

        builder.Services.AddSingleton<IOptionsChangeTokenSource<LbOptions>>(
            new ConfigurationChangeTokenSource<LbOptions>(lbSection));

        builder.Services.AddSingleton<IValidateOptions<LbOptions>, LbOptionsValidator>();

        builder.Services.AddSingleton<IBackendRegistry, BackendRegistry>();
        builder.Services.AddSingleton<ILoadBalancingPolicy, RoundRobinPolicy>();
        builder.Services.AddSingleton<IBytePump, StreamBytePump>();
        builder.Services.AddSingleton<IProxySession, ProxySession>();
        builder.Services.AddSingleton<IConnectionDispatcher, ConnectionDispatcher>();
        builder.Services.AddHostedService<TcpListenerService>();

        _host = builder.Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_host is null) throw new InvalidOperationException("Host not built.");
        await _host.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is null) return;

        await _host.StopAsync();
        _host.Dispose();
    }
}
