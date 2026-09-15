namespace DomainBlocks.EventStore.Metadata;

/// <summary>
/// Writes metadata entries for the event currently being contributed to. Keys are compared ordinally.
/// </summary>
public readonly ref struct MetadataWriter
{
    private readonly MetadataBuffer _buffer;

    internal MetadataWriter(MetadataBuffer buffer)
    {
        _buffer = buffer;
    }

    /// <summary>
    /// Sets an entry, replacing any existing value for the key.
    /// </summary>
    public void Set(string key, string value) => _buffer.Set(key, value);

    /// <summary>
    /// Adds an entry if the key is not already present.
    /// </summary>
    /// <returns><see langword="true"/> if the entry was added; <see langword="false"/> if the key already existed.</returns>
    public bool TryAdd(string key, string value) => _buffer.TryAdd(key, value);
}