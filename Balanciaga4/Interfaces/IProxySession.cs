using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Balanciaga4.Interfaces;

public interface IProxySession
{
    Task RunAsync(TcpClient clientTcpClient, IPEndPoint backendEndPoint, CancellationToken cancellationToken);
}
