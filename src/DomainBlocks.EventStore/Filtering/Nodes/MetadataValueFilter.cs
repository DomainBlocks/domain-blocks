using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Represents a filter that matches events whose metadata value for <see cref="Key"/> is one of <see cref="Values"/>.
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
    /// Gets the values, distinct and in ordinal order.
    /// </summary>
    public ImmutableArray<string> Values => _values.Values;

    public override bool Matches(IFilterableEvent filterable) =>
        filterable.TryGetMetadata(Key, out var value) && _values.Contains(value);

    public override string ToString() => Format(nameof(Metadata), [Key, .. Values]);
}