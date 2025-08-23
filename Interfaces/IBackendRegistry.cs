using System.Net;

namespace Balanciaga4.Interfaces;

public interface IBackendRegistry
{
    IReadOnlyList<IPEndPoint> GetHealthy();
    int IncrementActive(IPEndPoint be);
    int DecrementActive(IPEndPoint be);
}
