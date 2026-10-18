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

    public bool Contains(ReadOnlySpan<char> value)
    {
        var values = Values;

        if (values.Length == 1)
            return value.SequenceEqual(values[0]);

        // The values are in ordinal order, which is the order of an ordinal comparison of spans.
        var low = 0;
        var high = values.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var comparison = value.CompareTo(values[middle], StringComparison.Ordinal);

            if (comparison == 0)
                return true;

            if (comparison < 0)
                high = middle - 1;
            else
                low = middle + 1;
        }

        return false;
    }
}