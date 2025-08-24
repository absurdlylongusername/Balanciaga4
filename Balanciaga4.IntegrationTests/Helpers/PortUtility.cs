using System.Net;
using System.Net.Sockets;

namespace Balanciaga4.IntegrationTests.Helpers;

public static class PortUtility
{
    public static int GetFreeTcpPort()
    {
        // TODO: better way to do this?
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
