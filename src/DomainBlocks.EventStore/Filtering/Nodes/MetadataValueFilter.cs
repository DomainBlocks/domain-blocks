using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events whose metadata entry with the key <see cref="Key"/> has one of <see cref="Values"/>.
/// </summary>
public sealed class MetadataValueFilter : EventFilter
{
    private readonly StringSet _values;

    internal MetadataValueFilter(string key, StringSet values)
    {
        Key = key;
        _values = values;
    }

    public string Key { get; }

    /// <summary>
    /// The values, distinct and in ordinal order.
    /// </summary>
    public ImmutableArray<string> Values => _values.Values;

    public override bool Matches(IFilterableEvent filterable) =>
        filterable.TryGetMetadata(Key, out var value) && _values.Contains(value);
}