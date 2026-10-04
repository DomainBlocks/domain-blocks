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
    /// Determines whether this filter matches the specified event.
    /// </summary>
    public abstract bool Matches(IFilterableEvent filterable);

    // No store can hold a NUL character in a string or search for one.
    private static void ThrowIfContainsNul(string value, string paramName)
    {
        if (value.Contains('\0'))
            throw new ArgumentException("The value must not contain a NUL character.", paramName);
    }
}