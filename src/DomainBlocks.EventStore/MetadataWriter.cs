namespace DomainBlocks.EventStore;

public sealed class MetadataWriter
{
    private readonly Dictionary<string, string> _metadata;

    internal MetadataWriter(Dictionary<string, string> metadata)
    {
        _metadata = metadata;
    }

    public void Set(string key, string value) => _metadata[key] = value;

    public bool TryAdd(string key, string value) => _metadata.TryAdd(key, value);
}