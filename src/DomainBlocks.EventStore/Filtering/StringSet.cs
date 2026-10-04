using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// A set of strings compared ordinally. The values are kept distinct and in ordinal order, regardless of the order in
/// which they were given.
/// </summary>
internal readonly struct StringSet(IEnumerable<string> values)
{
    public ImmutableArray<string> Values { get; } =
        [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public bool Contains(string value)
    {
        var values = Values;

        // Most sets contain one value.
        return values.Length == 1
            ? string.Equals(values[0], value, StringComparison.Ordinal)
            : values.BinarySearch(value, StringComparer.Ordinal) >= 0;
    }
}