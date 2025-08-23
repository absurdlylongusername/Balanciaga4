namespace Balanciaga4.Interfaces;

public interface IBytePump
{
    Task PipeAsync(Stream source, Stream destination, CancellationToken cancellationToken);
}
