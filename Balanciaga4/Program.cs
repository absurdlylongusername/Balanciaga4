using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Balanciaga4.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

var lbSection = builder.Configuration.GetSection("LoadBalancer");

builder.Services.AddOptions<LbOptions>().ValidateOnStart(); // typed

builder.Services.AddSingleton<IConfigureOptions<LbOptions>>(sp =>
    new LbOptionsConfigurator(sp.GetRequiredService<ILogger<LbOptionsConfigurator>>(), lbSection));   // maps strings → typed

builder.Services.AddSingleton<IOptionsChangeTokenSource<LbOptions>>(
    new ConfigurationChangeTokenSource<LbOptions>(lbSection)); // reloads

builder.Services.AddSingleton<IValidateOptions<LbOptions>, LbOptionsValidator>();



// Core services
builder.Services.AddSingleton<IBackendRegistry, BackendRegistry>();
builder.Services.AddSingleton<ILoadBalancingPolicy, RoundRobinPolicy>(); // default; swappable later
builder.Services.AddSingleton<IBytePump, StreamBytePump>();
builder.Services.AddSingleton<IProxySession, ProxySession>();


// Dispatcher + Listener
builder.Services.AddSingleton<IConnectionDispatcher, ConnectionDispatcher>();
builder.Services.AddHostedService<TcpListenerService>();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; });

await builder.Build().RunAsync();
