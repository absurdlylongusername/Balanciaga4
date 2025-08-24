using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Balanciaga4.Interfaces;
using Balanciaga4.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Balanciaga4.Services;

public sealed class TcpHealthChecker : BackgroundService
{
    private IBackendRegistry Registry { get; }
    private IOptionsMonitor<LbOptions> Options { get; }
    private ILogger<TcpHealthChecker> Logger { get; }

    // per-backend counters (checker-local, not in registry)
    private ConcurrentDictionary<IPEndPoint, (int Failures, int Successes)> Counters { get; } = new();

    public TcpHealthChecker(IBackendRegistry registry, IOptionsMonitor<LbOptions> options, ILogger<TcpHealthChecker> logger)
    {
        Registry = registry;
        Options = options;
        Logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(Options.CurrentValue.HealthCheck.IntervalMs, 50)));
        try
        {
            while (await periodicTimer.WaitForNextTickAsync(stoppingToken))
            {
                var endpoints = Registry.GetAllEndpoints();
                foreach (var endpoint in endpoints)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var jitter = Options.CurrentValue.HealthCheck.JitterMs;
                    if (jitter > 0)
                    {
                        var delayMs = Random.Shared.Next(0, jitter);
                        if (delayMs > 0)
                        {
                            await Task.Delay(delayMs, stoppingToken);
                        }
                    }

                    var isUp = await ProbeOnceAsync(endpoint, stoppingToken);
                    UpdateState(endpoint, isUp);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // service is stopping
        }
    }

    private async Task<bool> ProbeOnceAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var timeoutMs = Math.Max(Options.CurrentValue.HealthCheck.ConnectTimeoutMs, 50);
        Logger.LogDebug("Probing {Endpoint} for health", endpoint);

        using var tcpClient = new TcpClient(endpoint.AddressFamily);
        try
        {
            var connectTask = tcpClient.ConnectAsync(endpoint, cancellationToken).AsTask();
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, cancellationToken));
            if (completed != connectTask)
            {
                Logger.LogDebug("Healthcheck on {Endpoint} timed out", endpoint);
                return false; // timeout
            }

            if (connectTask.IsFaulted)
            {
                Logger.LogDebug(connectTask.Exception ,"Healthcheck on {Endpoint} failed with exception", endpoint);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "ProbeOnce threw exception for some reason");
            return false;
        }
    }

    private void UpdateState(IPEndPoint endpoint, bool success)
    {
        var options = Options.CurrentValue.HealthCheck;

        var entry = Counters.AddOrUpdate(
            endpoint,
            _ => success ? (0, 1) : (1, 0),
            (_, old) =>
            {
                if (success)
                {
                    return (Failures: 0, Successes: old.Successes + 1);
                }

                return (Failures: old.Failures + 1, Successes: 0);
            });

        var nowUtc = DateTimeOffset.UtcNow;
        var info = Registry.GetInfo(endpoint);

        if (success)
        {
            if (info.Health == BackendHealth.Down && entry.Successes >= options.PassesToUp)
            {
                Registry.MarkAsUp(endpoint, $"Health probe succeeded ({entry.Successes}x)", nowUtc);
                Logger.LogInformation("Backend {Endpoint} marked UP after {SuccessCount} successes.", endpoint, entry.Successes);
            }
        }
        else
        {
            if (info.Health != BackendHealth.Down && entry.Failures >= options.FailsToDown)
            {
                Registry.MarkAsDown(endpoint, $"Health probe failed ({entry.Failures}x)", nowUtc);
                Logger.LogWarning("Backend {Endpoint} marked DOWN after {FailureCount} failures.", endpoint, entry.Failures);
            }
        }
    }
}
