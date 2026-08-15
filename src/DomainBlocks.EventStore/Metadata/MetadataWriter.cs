namespace DomainBlocks.EventStore.Metadata;

public readonly ref struct MetadataWriter
{
    private readonly Dictionary<string, string> _buffer;

    internal MetadataWriter(Dictionary<string, string> buffer)
    {
        _buffer = buffer;
    }

    public void Set(string key, string value) => _buffer[key] = value;

    public bool TryAdd(string key, string value) => _buffer.TryAdd(key, value);
}