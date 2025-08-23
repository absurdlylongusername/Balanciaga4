namespace Balanciaga4.Services;

/// <summary>
/// Decorator to have callback for streams whenever byte reads/writes occur
/// </summary>
/// <param name="innerStream"></param>
/// <param name="onReadWrite"></param>
public sealed class NotifyingStream(Stream innerStream, Action onReadWrite) : Stream
{
    public override bool CanRead => innerStream.CanRead;
    public override bool CanSeek => innerStream.CanSeek;
    public override bool CanWrite => innerStream.CanWrite;
    public override long Length => innerStream.Length;

    public override long Position
    {
        get => innerStream.Position;
        set => innerStream.Position = value;
    }

    public override void Flush()
    {
        innerStream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = innerStream.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            onReadWrite();
        }
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return innerStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        innerStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        innerStream.Write(buffer, offset, count);
        onReadWrite();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytesRead = await innerStream.ReadAsync(buffer, cancellationToken);
        if (bytesRead > 0)
        {
            onReadWrite();
        }
        return bytesRead;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await innerStream.WriteAsync(buffer, cancellationToken);
        onReadWrite();
    }

    public override ValueTask DisposeAsync()
    {
        return innerStream.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        innerStream.Dispose();
    }
}