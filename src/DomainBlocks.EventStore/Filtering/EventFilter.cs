using System.Collections.Immutable;
using System.Globalization;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Selects events by what is stored about them: the event name, the stream ID, the metadata, and the creation time.
/// </summary>
public abstract class EventFilter
{
    private protected EventFilter()
    {
    }

    /// <summary>
    /// Matches every event.
    /// </summary>
    public static EventFilter All { get; } = new AllEventsFilter();

    /// <summary>
    /// Matches no event.
    /// </summary>
    public static EventFilter None { get; } = new NoEventsFilter();

    /// <summary>
    /// Matches events whose name is one of <paramref name="eventNames"/>, using an ordinal comparison.
    /// </summary>
    public static EventFilter EventNames(params IEnumerable<string> eventNames)
    {
        ArgumentNullException.ThrowIfNull(eventNames);

        var names = new StringSet(eventNames);

        foreach (var name in names.Values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(eventNames));
            ThrowIfContainsNul(name, nameof(eventNames));
        }

        return names.Values.IsEmpty ? None : new EventNameFilter(names);
    }

    /// <summary>
    /// Matches events whose stream ID is one of <paramref name="streamIds"/>, using an ordinal comparison.
    /// </summary>
    public static EventFilter StreamIds(params IEnumerable<string> streamIds)
    {
        ArgumentNullException.ThrowIfNull(streamIds);

        var ids = new StringSet(streamIds);

        foreach (var id in ids.Values)
        {
            ArgumentException.ThrowIfNullOrEmpty(id, nameof(streamIds));
            ThrowIfContainsNul(id, nameof(streamIds));
        }

        return ids.Values.IsEmpty ? None : new StreamIdFilter(ids);
    }

    /// <summary>
    /// Matches events whose stream ID starts with <paramref name="prefix"/>, using an ordinal comparison.
    /// </summary>
    public static EventFilter StreamIdStartsWith(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ThrowIfContainsNul(prefix, nameof(prefix));

        return prefix.Length == 0 ? All : new StreamIdPrefixFilter(prefix);
    }

    /// <summary>
    /// Matches events that have a metadata entry with the key <paramref name="key"/>.
    /// </summary>
    public static EventFilter MetadataExists(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ThrowIfContainsNul(key, nameof(key));

        return new MetadataExistsFilter(key);
    }

    /// <summary>
    /// Matches events whose metadata entry with the key <paramref name="key"/> has one of <paramref name="values"/>,
    /// using an ordinal comparison.
    /// </summary>
    public static EventFilter Metadata(string key, params IEnumerable<string> values)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ThrowIfContainsNul(key, nameof(key));
        ArgumentNullException.ThrowIfNull(values);

        var set = new StringSet(values);

        foreach (var value in set.Values)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(values));
            ThrowIfContainsNul(value, nameof(values));
        }

        return set.Values.IsEmpty ? None : new MetadataValueFilter(key, set);
    }

    /// <summary>
    /// Matches events created at or after <paramref name="from"/>.
    /// </summary>
    public static EventFilter CreatedAtOrAfter(DateTimeOffset from) => new CreatedAtFilter(from, null);

    /// <summary>
    /// Matches events created before <paramref name="before"/>.
    /// </summary>
    public static EventFilter CreatedBefore(DateTimeOffset before) => new CreatedAtFilter(null, before);

    /// <summary>
    /// Matches events that match every one of <paramref name="filters"/>. With no filters, it matches every event.
    /// </summary>
    public static EventFilter AllOf(params IEnumerable<EventFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var result = All;

        foreach (var filter in filters)
            result &= filter;

        return result;
    }

    /// <summary>
    /// Matches events that match at least one of <paramref name="filters"/>. With no filters, it matches no event.
    /// </summary>
    public static EventFilter AnyOf(params IEnumerable<EventFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var result = None;

        foreach (var filter in filters)
            result |= filter;

        return result;
    }

    /// <summary>
    /// Matches events that match both filters. The left filter is evaluated first.
    /// </summary>
    public static EventFilter operator &(EventFilter left, EventFilter right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return (left, right) switch
        {
            (NoEventsFilter, _) or (_, NoEventsFilter) => None,
            (AllEventsFilter, _) => right,
            (_, AllEventsFilter) => left,
            _ => new AndFilter([.. ConjunctsOf(left), .. ConjunctsOf(right)])
        };
    }

    /// <summary>
    /// Matches events that match either filter. The left filter is evaluated first.
    /// </summary>
    public static EventFilter operator |(EventFilter left, EventFilter right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return (left, right) switch
        {
            (AllEventsFilter, _) or (_, AllEventsFilter) => All,
            (NoEventsFilter, _) => right,
            (_, NoEventsFilter) => left,
            _ => new OrFilter([.. DisjunctsOf(left), .. DisjunctsOf(right)])
        };
    }

    /// <summary>
    /// Matches events that do not match <paramref name="filter"/>.
    /// </summary>
    public static EventFilter operator !(EventFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return filter switch
        {
            AllEventsFilter => None,
            NoEventsFilter => All,
            NotFilter negation => negation.Operand,
            _ => new NotFilter(filter)
        };
    }

    /// <summary>
    /// Determines whether this filter matches the specified event.
    /// </summary>
    public abstract bool Matches(IFilterableEvent filterable);

    /// <summary>
    /// Returns a description of the filter for logs and messages, written as the expression that builds it.
    /// </summary>
    public abstract override string ToString();

    private protected static string Format(string name, IEnumerable<string> values) =>
        $"{name}({string.Join(", ", values.Select(Quote))})";

    // In UTC, so that an instant reads the same whatever offset it was given with.
    private protected static string Format(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    // Nested conjunctions and disjunctions are flattened, so combining filters one at a time builds a single node.
    private static ImmutableArray<EventFilter> ConjunctsOf(EventFilter filter) =>
        filter is AndFilter conjunction ? conjunction.Operands : [filter];

    private static ImmutableArray<EventFilter> DisjunctsOf(EventFilter filter) =>
        filter is OrFilter disjunction ? disjunction.Operands : [filter];

    // Escaped, so a value cannot be mistaken for the end of the string or for another value.
    private static string Quote(string value) => $"\"{value.Replace(@"\", @"\\").Replace("\"", "\\\"")}\"";

    // No store can hold a NUL character in a string or search for one.
    private static void ThrowIfContainsNul(string value, string paramName)
    {
        if (value.Contains('\0'))
            throw new ArgumentException("The value must not contain a NUL character.", paramName);
    }
}