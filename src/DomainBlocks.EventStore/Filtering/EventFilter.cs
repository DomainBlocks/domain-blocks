using System.Collections.Immutable;
using System.Globalization;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Represents a condition that selects events by name, stream ID, metadata, or creation time.
/// </summary>
public abstract class EventFilter
{
    private protected EventFilter()
    {
    }

    /// <summary>
    /// Gets a filter that matches every event.
    /// </summary>
    public static EventFilter All { get; } = new AllEventsFilter();

    /// <summary>
    /// Gets a filter that matches no events.
    /// </summary>
    public static EventFilter None { get; } = new NoEventsFilter();

    /// <summary>
    /// Creates a filter that matches events whose name is one of <paramref name="eventNames"/>, compared ordinally.
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
    /// Creates a filter that matches events whose stream ID is one of <paramref name="streamIds"/>, compared ordinally.
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
    /// Creates a filter that matches events whose stream ID starts with <paramref name="prefix"/>, compared ordinally.
    /// </summary>
    public static EventFilter StreamIdStartsWith(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ThrowIfContainsNul(prefix, nameof(prefix));

        return prefix.Length == 0 ? All : new StreamIdPrefixFilter(prefix);
    }

    /// <summary>
    /// Creates a filter that matches events with a metadata entry for <paramref name="key"/>.
    /// </summary>
    public static EventFilter MetadataExists(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ThrowIfContainsNul(key, nameof(key));

        return new MetadataExistsFilter(key);
    }

    /// <summary>
    /// Creates a filter that matches events whose metadata value for <paramref name="key"/> is one of
    /// <paramref name="values"/>, compared ordinally.
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
    /// Creates a filter that matches events created at or after <paramref name="from"/>.
    /// </summary>
    public static EventFilter CreatedAtOrAfter(DateTimeOffset from) => new CreatedAtFilter(from, null);

    /// <summary>
    /// Creates a filter that matches events created before <paramref name="before"/>.
    /// </summary>
    public static EventFilter CreatedBefore(DateTimeOffset before) => new CreatedAtFilter(null, before);

    /// <summary>
    /// Creates a filter that matches events that match all of <paramref name="filters"/>, or every event if there are
    /// none.
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
    /// Creates a filter that matches events that match any of <paramref name="filters"/>, or no events if there are
    /// none.
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
    /// Creates a filter that matches events that match both filters.
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
    /// Creates a filter that matches events that match either filter.
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
    /// Creates a filter that matches events that <paramref name="filter"/> does not match.
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
    /// Determines whether this filter matches an event.
    /// </summary>
    public abstract bool Matches(IFilterableEvent filterable);

    /// <summary>
    /// Returns the expression that builds this filter, for logs and messages.
    /// </summary>
    public abstract override string ToString();

    private protected static string Format(string name, IEnumerable<string> values) =>
        $"{name}({string.Join(", ", values.Select(Quote))})";

    // The instant is formatted in UTC, so equal instants format the same whatever offset they were given with.
    private protected static string Format(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    // Nested conjunctions and disjunctions are flattened, so combining filters one at a time builds a single node.
    private static ImmutableArray<EventFilter> ConjunctsOf(EventFilter filter) =>
        filter is AndFilter conjunction ? conjunction.Operands : [filter];

    private static ImmutableArray<EventFilter> DisjunctsOf(EventFilter filter) =>
        filter is OrFilter disjunction ? disjunction.Operands : [filter];

    // Backslashes and quotes are escaped, so a value cannot be mistaken for the closing quote or for another value.
    private static string Quote(string value) => $"\"{value.Replace(@"\", @"\\").Replace("\"", "\\\"")}\"";

    // PostgreSQL text cannot contain a NUL character, so no store accepts one in a filter value.
    private static void ThrowIfContainsNul(string value, string paramName)
    {
        if (value.Contains('\0'))
            throw new ArgumentException("The value must not contain a NUL character.", paramName);
    }
}