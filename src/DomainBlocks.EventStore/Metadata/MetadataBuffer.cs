namespace DomainBlocks.EventStore.Metadata;

/// <summary>
/// Accumulates the merged metadata of a batch of events in one flat array, handing each event a slice. Keys are
/// de-duplicated within the current event's slice by a linear scan, which beats hashing for the handful of entries
/// metadata carries and needs no per-event allocation.
/// </summary>
/// <remarks>
/// Growing the array copies the entries so far into a new one; slices already handed out keep pointing at the old
/// array, which is never written to again, so they stay valid.
/// </remarks>
internal sealed class MetadataBuffer(int initialCapacity = 16)
{
    private KeyValuePair<string, string>[] _entries = new KeyValuePair<string, string>[initialCapacity];
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
        _entries.AsMemory(_eventStart, _count - _eventStart);

    public void Set(string key, string value)
    {
        var index = IndexOf(key);

        if (index >= 0)
            _entries[index] = new KeyValuePair<string, string>(key, value);
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

    private int IndexOf(string key)
    {
        var entries = _entries;

        for (var i = _eventStart; i < _count; i++)
        {
            if (string.Equals(entries[i].Key, key, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private void Append(string key, string value)
    {
        if (_count == _entries.Length)
            Array.Resize(ref _entries, _entries.Length * 2);

        _entries[_count++] = new KeyValuePair<string, string>(key, value);
    }
}