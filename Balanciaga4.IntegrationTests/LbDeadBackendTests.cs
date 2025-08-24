using System.Net;
using Balanciaga4.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;

namespace Balanciaga4.IntegrationTests;

public sealed class LbDeadBackendTests
{
    private ILogger Logger { get; } = NUnitLogger.CreateLogger(nameof(LbDeadBackendTests));

    [Test]
    public async Task When_A_Backend_Is_Down_Some_Requests_Fail_Without_HealthChecks()
    {
        var portA = PortUtility.GetFreeTcpPort();
        var portB = PortUtility.GetFreeTcpPort();

        await using var serverA = new BackendServer(Logger, portA, "A");
        await serverA.StartAsync();

        // B is intentionally NOT started

        var listenPort = PortUtility.GetFreeTcpPort();
        IPEndPoint[] backends = [new(IPAddress.Loopback, portA), new(IPAddress.Loopback, portB)];

        await using var loadBalancer = new LoadBalancerHost(listenPort, backends);
        await loadBalancer.StartAsync();

        using var http = TestHelpers.CreateHttpClient();

        var successes = 0;
        var failures = 0;

        for (var i = 0; i < 10; i++)
        {
            try
            {
                var s = await http.GetStringAsync($"http://localhost:{listenPort}/");
                if (s is "A" or "B") successes++;
            }
            catch
            {
                failures++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(successes, Is.GreaterThan(0));
            Assert.That(failures, Is.GreaterThan(0));
        });
    }
}