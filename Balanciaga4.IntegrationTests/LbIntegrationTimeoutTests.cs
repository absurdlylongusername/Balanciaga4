using System.Net;
using System.Net.Sockets;
using Balanciaga4.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;

namespace Balanciaga4.IntegrationTests;

public sealed class LbIntegrationTimeoutTests
{
    private ILogger Logger { get; } = NUnitLogger.CreateLogger(nameof(LbDeadBackendTests));

    [Test]
    public async Task Slow_Client_Should_Still_Complete_Download()
    {
        const int size = 1 * 512 * 512; // With this size it should take about 8s to pass
        var portA = PortUtility.GetFreeTcpPort();
        await using var serverA = new BackendServer(Logger, portA, "A");
        var file = Path.Combine(Path.GetTempPath(), $"slow-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(file, new byte[size]);
        await serverA.StartAsync(file);

        var portB = PortUtility.GetFreeTcpPort();
        await using var serverB = new BackendServer(Logger, portB, "B");
        await serverB.StartAsync();

        var listenPort = PortUtility.GetFreeTcpPort();
        IPEndPoint[] backends = [new(IPAddress.Loopback, portA), new(IPAddress.Loopback, portB)];

        await using var loadBalancer = new LoadBalancerHost(listenPort, backends);
        await loadBalancer.StartAsync();

        using var client = TestHelpers.CreateHttpClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        using var response = await client.GetAsync($"http://localhost:{listenPort}/big.bin", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();

        var buffer = new byte[512]; // tiny reads
        long total = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer)) > 0)
        {
            total += n;
            await Task.Delay(2); // throttle to force backpressure through LB
        }

        Assert.That(total, Is.EqualTo(size));
        File.Delete(file);
    }

    [Test]
    public async Task Idle_Connection_Should_Close_After_IdleMs()
    {
        var portA = PortUtility.GetFreeTcpPort();
        await using var serverA = new BackendServer(Logger, portA, "A");
        await serverA.StartAsync();

        var portB = PortUtility.GetFreeTcpPort();
        await using var serverB = new BackendServer(Logger, portB, "B");
        await serverB.StartAsync();

        var listenPort = PortUtility.GetFreeTcpPort();
        IPEndPoint[] backends = [new(IPAddress.Loopback, portA), new(IPAddress.Loopback, portB)];

        // Set a small idle timeout (2s) to make the test quick
        await using var loadBalancer = new LoadBalancerHost(listenPort, backends, idleMs: 2000);
        await loadBalancer.StartAsync();

        // Open a raw TCP connection and stay idle
        using var client = new TcpClient();
        await client.ConnectAsync("localhost", listenPort);

        var networkStream = client.GetStream();
        var buffer = new byte[1];

        // Read with a generous timeout; expect it to complete with 0 bytes or throw after LB closes
        var readTask = networkStream.ReadAsync(buffer).AsTask();

        // Wait > idle to let LB close it, then observe the read task
        await Task.Delay(3000);

        // The read should complete (0) or fault (IOException) because the LB closed the connection
        var completed = readTask.IsCompleted ? readTask : await Task.WhenAny(readTask, Task.Delay(5000)) as Task<int>;
        Assert.That(completed, Is.Not.Null);
        Assert.That(completed.IsCompleted, Is.True);

        if (readTask.IsFaulted)
        {
            // Accept IOException/SocketException as valid closure signal
            Assert.That(readTask.Exception, Is.Not.Null);
            Assert.That(readTask.Exception.GetBaseException(), Is.InstanceOf<IOException>());
        }
        else
        {
            // A clean close typically returns 0 bytes
            Assert.That(readTask.Result, Is.EqualTo(0));
        }
    }
}