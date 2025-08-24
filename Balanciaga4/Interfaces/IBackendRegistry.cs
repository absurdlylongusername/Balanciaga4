using System.Net;

namespace Balanciaga4.Interfaces;

public interface IBackendRegistry
{
    IReadOnlyList<IPEndPoint> GetAllEndpoints();
    IReadOnlyList<IPEndPoint> GetHealthyEndpoints();
    BackendInfo GetInfo(IPEndPoint endpoint);
    void IncrementConnection(IPEndPoint backendEndpoint);
    void DecrementConnection(IPEndPoint backendEndpoint);

    void MarkAsUp(IPEndPoint endpoint, string reason, DateTimeOffset nowUtc);   // new
    void MarkAsDown(IPEndPoint endpoint, string reason, DateTimeOffset nowUtc); // new
}
