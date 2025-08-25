using System.Buffers;
using Balanciaga4.Interfaces;

namespace Balanciaga4.Services;

public sealed class StreamBytePump : IBytePump
{
    private const int BufferSizeBytes = 16 * 1024;

    public async Task PipeAsync(Stream sourceStream, Stream destinationStream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSizeBytes);

        try
        {
            while (true)
            {
                var bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, BufferSizeBytes), cancellationToken);
                if (bytesRead == 0)
                {
                    break; // EOF from source
                }

                await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
