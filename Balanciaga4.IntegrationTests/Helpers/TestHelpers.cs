namespace Balanciaga4.IntegrationTests.Helpers;

public static class TestHelpers
{
    private static SocketsHttpHandler HttpHandler => new()
    {
        ResponseDrainTimeout = TimeSpan.FromMilliseconds(50),

        // Optional: ensures we never reuse pooled sockets between requests
        PooledConnectionLifetime = TimeSpan.Zero,
        PooledConnectionIdleTimeout = TimeSpan.Zero
    };

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(HttpHandler, disposeHandler: true);
        client.DefaultRequestHeaders.ConnectionClose = true;
        return client;
    }
}