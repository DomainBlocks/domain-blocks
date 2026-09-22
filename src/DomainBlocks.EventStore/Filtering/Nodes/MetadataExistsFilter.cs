using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events that have a metadata entry with the key <see cref="Key"/>.
/// </summary>
public sealed record MetadataExistsFilter : EventFilter
{
    internal MetadataExistsFilter(string key)
    {
        Key = key;
    }

    public string Key { get; }

    internal override int Cost => 1;

    public override bool Matches(IFilterableEvent filterable) => filterable.TryGetMetadata(Key, out _);

    protected override void WriteTo(StringBuilder text) => Write(text, "metadataExists", [Key]);
}