using System.Net.Sockets;

namespace Balanciaga4.Interfaces;

public interface IConnectionDispatcher
{
    Task DispatchAsync(TcpClient client, CancellationToken cancellationToken);
}