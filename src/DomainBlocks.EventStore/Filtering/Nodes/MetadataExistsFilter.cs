namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events that have a metadata entry with the key <see cref="Key"/>.
/// </summary>
public sealed class MetadataExistsFilter : EventFilter
{
    internal MetadataExistsFilter(string key)
    {
        Key = key;
    }

    public string Key { get; }

    public override bool Matches(IFilterableEvent filterable) => filterable.TryGetMetadata(Key, out _);

    public override string ToString() => Format(nameof(MetadataExists), [Key]);
}