using System.Net;
using Balanciaga4.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;

namespace Balanciaga4.IntegrationTests;

public sealed class LbIntegrationTests
{
    private ILogger Logger { get; } = NUnitLogger.CreateLogger(nameof(LbIntegrationTests));
    private static SocketsHttpHandler HttpHandler => new()
    {
        // Retire the connection immediately rather than lingering for 2s
        ResponseDrainTimeout = TimeSpan.FromMilliseconds(50),

        // Optional: ensures we never reuse pooled sockets between requests
        PooledConnectionLifetime = TimeSpan.Zero,
        PooledConnectionIdleTimeout = TimeSpan.Zero
    };

    [Test]
    public async Task RoundRobin_Should_Alternate_Backends()
    {
        var portA = PortUtility.GetFreeTcpPort();
        var portB = PortUtility.GetFreeTcpPort();
        await using var serverA = new BackendServer(Logger, portA, "A");
        await using var serverB = new BackendServer(Logger, portB, "B");
        await serverA.StartAsync();
        await serverB.StartAsync();

        var listenPort = PortUtility.GetFreeTcpPort();
        IPEndPoint[] backends = [new(IPAddress.Loopback, portA), new(IPAddress.Loopback, portB)];

        await using var loadBalancer = new LoadBalancerHost(listenPort, backends);
        Logger.LogInformation("Starting load balancer {Port}", listenPort);
        await loadBalancer.StartAsync();
        Logger.LogInformation("Started load balancer{Port}", listenPort);

        using var client = new HttpClient(HttpHandler, disposeHandler: true);
        client.DefaultRequestHeaders.ConnectionClose = true;
        Logger.LogInformation("Sending message 1 to http://localhost:{listenPort}/", listenPort);
        var r1 = await client.GetStringAsync($"http://localhost:{listenPort}/");
        Logger.LogInformation("Sending message 2 to http://localhost:{listenPort}/", listenPort);
        var r2 = await client.GetStringAsync($"http://localhost:{listenPort}/");
        Logger.LogInformation("Sending message 3 to http://localhost:{listenPort}/", listenPort);
        var r3 = await client.GetStringAsync($"http://localhost:{listenPort}/");
        Logger.LogInformation("Sending message 4 to http://localhost:{listenPort}/", listenPort);
        var r4 = await client.GetStringAsync($"http://localhost:{listenPort}/");
        var debug = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                                                                  $"http://localhost:{listenPort}/"));
        foreach (var httpResponseHeader in debug.Headers)
        {
            Logger.LogInformation("Header: {Header}\nValue: {Value}", httpResponseHeader.Key, httpResponseHeader.Value);
        }

        Assert.That((r1, r2, r3, r4), Is.EqualTo(("A", "B", "A", "B")));
    }

    [Test]
    public async Task Large_File_Should_Proxy_Successfully()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"big-{Guid.NewGuid():N}.bin");
        try
        {
            var size = 10 * 1024 * 1024;
            var random = new byte[size];
            new Random().NextBytes(random);
            await File.WriteAllBytesAsync(tmp, random);

            var portA = PortUtility.GetFreeTcpPort();
            await using var beA = new BackendServer(Logger, portA, "A");
            await beA.StartAsync(tmp);

            var portB = PortUtility.GetFreeTcpPort();
            await using var beB = new BackendServer(Logger, portB, "B");
            await beB.StartAsync();

            var listenPort = PortUtility.GetFreeTcpPort();
            var backends = new[] { new IPEndPoint(IPAddress.Loopback, portA), new IPEndPoint(IPAddress.Loopback, portB) };

            await using var lb = new LoadBalancerHost(listenPort, backends);
            await lb.StartAsync();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            using var response = await http.GetAsync($"http://localhost:{listenPort}/big.bin", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var total = 0L;
            var buffer = new byte[8192];
            int n;
            while ((n = await stream.ReadAsync(buffer)) > 0)
            {
                total += n;
            }

            Assert.That(total, Is.EqualTo(size));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}