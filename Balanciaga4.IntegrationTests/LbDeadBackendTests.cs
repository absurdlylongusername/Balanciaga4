using System.Net;
using Balanciaga4.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;

namespace Balanciaga4.IntegrationTests;

public sealed class LbDeadBackendTests
{
    private ILogger Logger { get; } = NUnitLogger.CreateLogger(nameof(LbDeadBackendTests));

    [SetUp]
    public void Setup()
    {

    }

    [Test]
    public async Task When_A_Backend_Is_Down_Some_Requests_Fail_Without_HealthChecks()
    {
        var portA = PortUtility.GetFreeTcpPort();
        var portB = PortUtility.GetFreeTcpPort();

        await using var beA = new BackendServer(Logger, portA, "A");
        await beA.StartAsync();

        // B is intentionally NOT started

        var listenPort = PortUtility.GetFreeTcpPort();
        var backends = new[] { new IPEndPoint(IPAddress.Loopback, portA), new IPEndPoint(IPAddress.Loopback, portB) };

        await using var lb = new LoadBalancerHost(listenPort, backends);
        await lb.StartAsync();

        using var http = new HttpClient();

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
        Assert.That(successes, Is.GreaterThan(0));
        Assert.That(failures, Is.GreaterThan(0));
    }
}