using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// A distinct set of strings in ordinal order.
/// </summary>
internal readonly struct StringSet(IEnumerable<string> values)
{
    public ImmutableArray<string> Values { get; } =
        [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public bool Contains(string value)
    {
        var values = Values;

        // A stream subscription filters on a single stream ID, so a set with one value is compared directly.
        return values.Length == 1
            ? string.Equals(values[0], value, StringComparison.Ordinal)
            : values.BinarySearch(value, StringComparer.Ordinal) >= 0;
    }
}