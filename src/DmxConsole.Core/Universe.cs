namespace DmxConsole.Core;

/// <summary>
/// A single DMX512 universe: 512 one-byte channels (1-512 in DMX terms, 0-511 here).
/// Holds only the current output buffer - no knowledge of how it was computed.
/// </summary>
public sealed class Universe
{
    public const int ChannelCount = 512;

    public int Id { get; }
    public string Name { get; set; }

    private readonly byte[] _buffer = new byte[ChannelCount];
    private readonly object _lock = new();

    public Universe(int id, string? name = null)
    {
        Id = id;
        Name = name ?? $"Universe {id}";
    }

    /// <summary>Gets or sets a single channel value (0-based index, 0..511).</summary>
    public byte this[int channelIndex]
    {
        get
        {
            ValidateIndex(channelIndex);
            lock (_lock)
            {
                return _buffer[channelIndex];
            }
        }
        set
        {
            ValidateIndex(channelIndex);
            lock (_lock)
            {
                _buffer[channelIndex] = value;
            }
        }
    }

    /// <summary>Returns a snapshot copy of the 512-byte buffer, safe to hand off to a sender thread.</summary>
    public byte[] Snapshot()
    {
        lock (_lock)
        {
            var copy = new byte[ChannelCount];
            Array.Copy(_buffer, copy, ChannelCount);
            return copy;
        }
    }

    /// <summary>Overwrites the whole buffer at once (e.g. from the merge engine).</summary>
    public void CopyFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length != ChannelCount)
            throw new ArgumentException($"Universe buffer must be exactly {ChannelCount} bytes.", nameof(source));

        lock (_lock)
        {
            source.CopyTo(_buffer);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer);
        }
    }

    private static void ValidateIndex(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channelIndex), $"DMX channel index must be in range 0..{ChannelCount - 1}.");
    }
}
