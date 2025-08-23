using System.Net;

namespace Balanciaga4.Interfaces;

public interface IBackendRegistry
{
    IReadOnlyList<IPEndPoint> GetHealthyEndpoints();
    int IncrementConnection(IPEndPoint backendEndpoint);
    int DecrementConnection(IPEndPoint backendEndpoint);
}
