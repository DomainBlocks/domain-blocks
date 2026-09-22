using System.Collections.Immutable;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Selects events using values available in storage, such as event name, stream, metadata, creation time, and payload
/// values.
/// </summary>
public abstract record EventFilter
{
    /// <summary>
    /// Matches every event.
    /// </summary>
    public static readonly EventFilter All = new AllEventsFilter();

    /// <summary>
    /// Matches no event.
    /// </summary>
    public static readonly EventFilter None = new NoEventsFilter();

    private protected EventFilter()
    {
    }

    /// <summary>
    /// The cost of evaluating this filter, based on what must be read from the event. 0 requires only always-available
    /// values, 1 metadata, 2 the stored payload, and 3 the decoded event. Conjunctions and disjunctions evaluate
    /// cheaper operands first and stop once the result is known.
    /// </summary>
    internal virtual int Cost => 0;

    /// <summary>
    /// Matches events stored under <paramref name="eventName"/>.
    /// </summary>
    public static EventFilter EventName(string eventName) => EventNames(eventName);

    /// <summary>
    /// Matches events stored under any of <paramref name="eventNames"/>.
    /// </summary>
    public static EventFilter EventNames(params IEnumerable<string> eventNames)
    {
        ArgumentNullException.ThrowIfNull(eventNames);

        var names = new StringSet(eventNames);

        foreach (var name in names.Values)
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(eventNames));

        return names.Values.IsEmpty ? None : new EventNameFilter(names);
    }

    /// <summary>
    /// Matches events that are assignable to <typeparamref name="TEvent"/>.
    /// </summary>
    public static EventFilter OfType<TEvent>() where TEvent : notnull =>
        new EventTypeFilter(typeof(TEvent), null, null);

    /// <summary>
    /// Matches events that are assignable to <typeparamref name="TEvent"/> for which <paramref name="predicate"/> is
    /// true.
    /// </summary>
    public static EventFilter OfType<TEvent>(Expression<Func<TEvent, bool>> predicate) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return new EventTypeFilter(typeof(TEvent), predicate, () =>
        {
            var compiled = predicate.Compile();
            return e => compiled((TEvent)e);
        });
    }

    /// <summary>
    /// Matches events in the stream <paramref name="streamId"/>.
    /// </summary>
    public static EventFilter StreamId(string streamId) => StreamIds(streamId);

    /// <summary>
    /// Matches events in any of the streams <paramref name="streamIds"/>.
    /// </summary>
    public static EventFilter StreamIds(params IEnumerable<string> streamIds)
    {
        ArgumentNullException.ThrowIfNull(streamIds);

        var ids = new StringSet(streamIds);

        foreach (var id in ids.Values)
            ArgumentException.ThrowIfNullOrEmpty(id, nameof(streamIds));

        return ids.Values.IsEmpty ? None : new StreamIdFilter(ids);
    }

    /// <summary>
    /// Matches events in streams whose id starts with <paramref name="prefix"/>, compared ordinally.
    /// </summary>
    public static EventFilter StreamIdStartsWith(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        return prefix.Length == 0 ? All : new StreamIdPrefixFilter(prefix);
    }

    /// <summary>
    /// Matches events that have a metadata entry with the key <paramref name="key"/>, whatever its value.
    /// </summary>
    public static EventFilter MetadataExists(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        return new MetadataExistsFilter(key);
    }

    /// <summary>
    /// Matches events whose metadata entry with the key <paramref name="key"/> has any of <paramref name="values"/>,
    /// compared ordinally.
    /// </summary>
    public static EventFilter Metadata(string key, params IEnumerable<string> values)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(values);

        var set = new StringSet(values);

        foreach (var value in set.Values)
            ArgumentNullException.ThrowIfNull(value, nameof(values));

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
    /// Matches events that every one of <paramref name="filters"/> matches. Matches every event when there are no
    /// filters.
    /// </summary>
    public static EventFilter AllOf(params IEnumerable<EventFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        return filters.Aggregate(All, static (result, filter) => result & filter);
    }

    /// <summary>
    /// Matches events that any one of <paramref name="filters"/> matches. Matches no events when there are no filters.
    /// </summary>
    public static EventFilter AnyOf(params IEnumerable<EventFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        return filters.Aggregate(None, static (result, filter) => result | filter);
    }

    /// <summary>
    /// Matches the events that both filters match.
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
    /// Matches the events that either filter matches.
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
    /// Matches events that <paramref name="filter"/> does not match.
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
    /// Lowers event type filters to filters on the stored event names that can be evaluated during pushdown. A
    /// predicate, when present, remains attached to the event type filter.
    /// </summary>
    /// <param name="eventNamesResolver">Resolves the stored event names for an event type.</param>
    public EventFilter LowerEventTypes(Func<Type, IReadOnlyCollection<string>> eventNamesResolver)
    {
        ArgumentNullException.ThrowIfNull(eventNamesResolver);

        return Rewrite((leaf, _) => leaf switch
        {
            EventTypeFilter { Predicate: null } byType => EventNames(eventNamesResolver(byType.EventType)),
            EventTypeFilter byType => EventNames(eventNamesResolver(byType.EventType)) & byType,
            _ => leaf
        });
    }

    /// <summary>
    /// Gets the leaf filters beneath this filter's logical operators.
    /// </summary>
    internal IEnumerable<EventFilter> GetLeafNodes()
    {
        return this switch
        {
            AndFilter conjunction => conjunction.Operands.SelectMany(x => x.GetLeafNodes()),
            OrFilter disjunction => disjunction.Operands.SelectMany(x => x.GetLeafNodes()),
            NotFilter negation => negation.Operand.GetLeafNodes(),
            _ => [this]
        };
    }

    /// <summary>
    /// Rebuilds the filter, replacing each leaf with the result of <paramref name="rewriter"/>.
    /// <paramref name="isNegated"/> indicates whether the leaf is under an odd number of negations.
    /// </summary>
    internal EventFilter Rewrite(Func<EventFilter, bool, EventFilter> rewriter, bool isNegated = false)
    {
        return this switch
        {
            AndFilter conjunction => AllOf(conjunction.Operands.Select(x => x.Rewrite(rewriter, isNegated))),
            OrFilter disjunction => AnyOf(disjunction.Operands.Select(x => x.Rewrite(rewriter, isNegated))),
            NotFilter negation => !negation.Operand.Rewrite(rewriter, !isNegated),
            _ => rewriter(this, isNegated)
        };
    }

    /// <summary>
    /// Whether the filter matches the event.
    /// </summary>
    public abstract bool Matches(IFilterableEvent filterable);

    /// <summary>
    /// Returns a stable textual representation of the filter. Equal filters produce the same text.
    /// </summary>
    public sealed override string ToString()
    {
        var text = new StringBuilder();
        WriteTo(text);
        return text.ToString();
    }

    protected abstract void WriteTo(StringBuilder text);

    private protected static void Write(StringBuilder text, string name, IEnumerable<string> values)
    {
        text.Append(name).Append('(');

        var isFirst = true;

        foreach (var value in values)
        {
            if (!isFirst)
                text.Append(',');

            // Quote and escape values so they cannot be confused with filter syntax or with other values.
            text.Append('"').Append(value.Replace("\\", @"\\").Replace("\"", "\\\"")).Append('"');
            isFirst = false;
        }

        text.Append(')');
    }

    private protected static void Write(StringBuilder text, string name, IEnumerable<EventFilter> operands)
    {
        text.Append(name).Append('(');

        var isFirst = true;

        foreach (var operand in operands)
        {
            if (!isFirst)
                text.Append(',');

            operand.WriteTo(text);
            isFirst = false;
        }

        text.Append(')');
    }

    // Write in UTC so the same instant has the same representation regardless of its original offset.
    private protected static string Write(DateTimeOffset? instant) =>
        instant?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "*";

    // Preserve the original order when operands have equal cost.
    private protected static ImmutableArray<EventFilter> CheapestFirst(IEnumerable<EventFilter> operands) =>
        [.. operands.OrderBy(x => x.Cost)];

    private protected static int GetHashCode(Type type, ImmutableArray<EventFilter> operands)
    {
        var hashCode = new HashCode();
        hashCode.Add(type);

        foreach (var operand in operands)
            hashCode.Add(operand);

        return hashCode.ToHashCode();
    }

    // Flatten nested conjunctions and disjunctions.
    private static IEnumerable<EventFilter> ConjunctsOf(EventFilter filter) =>
        filter is AndFilter conjunction ? conjunction.Operands : [filter];

    private static IEnumerable<EventFilter> DisjunctsOf(EventFilter filter) =>
        filter is OrFilter disjunction ? disjunction.Operands : [filter];
}