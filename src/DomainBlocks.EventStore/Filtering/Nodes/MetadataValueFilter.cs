using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events whose metadata entry with the key <see cref="Key"/> has any of <see cref="Values"/>.
/// </summary>
public sealed record MetadataValueFilter : EventFilter
{
    private readonly StringSet _values;

    internal MetadataValueFilter(string key, StringSet values)
    {
        Key = key;
        _values = values;
    }

    public string Key { get; }

    public IReadOnlyList<string> Values => _values.Values;

    internal override int Cost => 1;

    public override bool Matches(IFilterableEvent filterable) =>
        filterable.TryGetMetadata(Key, out var value) && _values.Contains(value);

    protected override void WriteTo(StringBuilder text) => Write(text, "metadata", [Key, .. _values.Values]);
}