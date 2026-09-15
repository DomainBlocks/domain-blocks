using System.Buffers;

namespace DomainBlocks.EventStore.Metadata;

/// <summary>
/// Accumulates the merged metadata of a batch of events in pooled, fixed-size chunks, handing each event a slice of
/// one chunk. Keys are de-duplicated within the current event's slice by a linear scan, which beats hashing for the
/// handful of entries metadata carries and needs no per-event allocation. Chunks are small enough to stay off the
/// large object heap and are returned to the pool on dispose, so a warmed-up batch allocates nothing for metadata.
/// </summary>
/// <remarks>
/// Slices are valid only until the buffer is disposed. The owner disposes it once the store has consumed the batch.
/// </remarks>
internal sealed class MetadataBuffer : IDisposable
{
    // 256 entries of two references is 4 KB on 64-bit, well under the LOH threshold.
    private const int ChunkSize = 256;

    private static readonly ArrayPool<KeyValuePair<string, string>> Pool =
        ArrayPool<KeyValuePair<string, string>>.Shared;

    private readonly List<KeyValuePair<string, string>[]> _chunks = [];
    private KeyValuePair<string, string>[] _chunk = [];
    private int _count;
    private int _eventStart;

    /// <summary>
    /// Starts a new event's slice.
    /// </summary>
    public void BeginEvent() => _eventStart = _count;

    /// <summary>
    /// Ends the current event's slice and returns it.
    /// </summary>
    public ReadOnlyMemory<KeyValuePair<string, string>> EndEvent() =>
        _chunk.AsMemory(_eventStart, _count - _eventStart);

    public void Set(string key, string value)
    {
        var index = IndexOf(key);

        if (index >= 0)
            _chunk[index] = new KeyValuePair<string, string>(key, value);
        else
            Append(key, value);
    }

    public bool TryAdd(string key, string value)
    {
        if (IndexOf(key) >= 0)
            return false;

        Append(key, value);
        return true;
    }

    public void Dispose()
    {
        foreach (var chunk in _chunks)
            Pool.Return(chunk, clearArray: true);

        _chunks.Clear();
        _chunk = [];
        _count = 0;
        _eventStart = 0;
    }

    private int IndexOf(string key)
    {
        var chunk = _chunk;

        for (var i = _eventStart; i < _count; i++)
        {
            if (string.Equals(chunk[i].Key, key, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private void Append(string key, string value)
    {
        if (_count == _chunk.Length)
            MoveEventToNewChunk();

        _chunk[_count++] = new KeyValuePair<string, string>(key, value);
    }

    /// <summary>
    /// Rents a new chunk and carries the current event's entries over to it, so an event's slice never spans two
    /// chunks. An event with more entries than a chunk holds gets a chunk of its own size.
    /// </summary>
    private void MoveEventToNewChunk()
    {
        var eventLength = _count - _eventStart;
        var chunk = Pool.Rent(Math.Max(ChunkSize, eventLength * 2));
        _chunks.Add(chunk);

        if (eventLength > 0)
            _chunk.AsSpan(_eventStart, eventLength).CopyTo(chunk);

        _chunk = chunk;
        _eventStart = 0;
        _count = eventLength;
    }
}