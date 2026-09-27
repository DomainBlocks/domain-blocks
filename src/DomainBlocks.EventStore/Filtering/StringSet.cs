using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// A set of strings kept in ordinal order without duplicates, so equal sets have equal values and a stable
/// representation regardless of the order in which the strings were given.
/// </summary>
internal readonly struct StringSet(IEnumerable<string> values) : IEquatable<StringSet>
{
    // Empty for the default value of the struct.
    public ImmutableArray<string> Values
    {
        get => field.IsDefault ? [] : field;
    } = [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public bool Contains(string value)
    {
        var values = Values;

        // Most sets contain one value.
        return values.Length == 1
            ? string.Equals(values[0], value, StringComparison.Ordinal)
            : values.BinarySearch(value, StringComparer.Ordinal) >= 0;
    }

    public bool Equals(StringSet other) => Values.SequenceEqual(other.Values);

    public override bool Equals(object? obj) => obj is StringSet other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        foreach (var value in Values)
            hashCode.Add(value, StringComparer.Ordinal);

        return hashCode.ToHashCode();
    }
}