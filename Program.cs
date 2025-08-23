using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Balanciaga4.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Bind options with normal .NET precedence (appsettings, env, CLI, etc)
// builder.Services.Configure<LbOptions>(builder.Configuration.GetSection("LoadBalancer"));

// Bind raw config (strings) with normal precedence
builder.Services.AddOptions<LbOptionsRaw>()
    .Bind(builder.Configuration.GetSection("LoadBalancer"))
    .ValidateOnStart();

// Map raw → typed, and validate typed options
builder.Services.AddSingleton<IConfigureOptions<LbOptions>, LbOptionsConfigurator>();
builder.Services.AddSingleton<IValidateOptions<LbOptions>, LbOptionsValidator>();
builder.Services.AddOptions<LbOptions>().ValidateOnStart();


// Core services
builder.Services.AddSingleton<IBackendRegistry, BackendRegistry>();
builder.Services.AddSingleton<ILoadBalancingPolicy, RoundRobinPolicy>(); // default; swappable later
builder.Services.AddSingleton<IBytePump, StreamBytePump>();

// Dispatcher + Listener
builder.Services.AddSingleton<IConnectionDispatcher, ConnectionDispatcher>();
builder.Services.AddHostedService<TcpListenerService>();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });

await builder.Build().RunAsync();
