using System.Buffers;

namespace DomainBlocks.EventStore.Metadata;

/// <summary>
/// Holds the merged metadata of an event batch in pooled chunks, with one slice per event, so it does not allocate for
/// each event. Keys are deduplicated within each event's slice. A slice is valid until the buffer is disposed.
/// </summary>
internal sealed class MetadataBuffer : IDisposable
{
    // A chunk of 256 entries, each holding two references, takes 4 KB on 64-bit, which is well under the large object
    // heap threshold.
    private const int ChunkSize = 256;

    private static readonly ArrayPool<KeyValuePair<string, string>> Pool =
        ArrayPool<KeyValuePair<string, string>>.Shared;

    private readonly List<KeyValuePair<string, string>[]> _chunks = [];
    private KeyValuePair<string, string>[] _chunk = [];
    private int _count;
    private int _eventStart;

    public void BeginEvent() => _eventStart = _count;

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

    // The current event's entries move to the new chunk, so a slice never spans two chunks.
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