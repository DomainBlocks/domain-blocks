using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

/// <summary>
/// Independently determines whether a filter matches an event. Each filter's semantics are expressed here so the filter
/// implementation can be tested against an independent definition of its behavior.
/// </summary>
internal static class EventFilterOracle
{
    private static readonly ConcurrentDictionary<LambdaExpression, Delegate> Predicates = new();

    public static bool Matches(EventFilter filter, StoredEvent e)
    {
        return filter switch
        {
            AllEventsFilter => true,
            NoEventsFilter => false,
            AndFilter conjunction => conjunction.Operands.All(x => Matches(x, e)),
            OrFilter disjunction => disjunction.Operands.Any(x => Matches(x, e)),
            NotFilter negation => !Matches(negation.Operand, e),
            EventNameFilter byName => byName.Names.Contains(e.EventName, StringComparer.Ordinal),
            StreamIdFilter byStream => byStream.Ids.Contains(e.StreamId, StringComparer.Ordinal),
            StreamIdPrefixFilter byPrefix => e.StreamId.StartsWith(byPrefix.Prefix, StringComparison.Ordinal),
            MetadataExistsFilter byKey => e.Metadata.ContainsKey(byKey.Key),

            MetadataValueFilter byValue => e.Metadata.TryGetValue(byValue.Key, out var value) &&
                                           byValue.Values.Contains(value, StringComparer.Ordinal),

            CreatedAtFilter byTime => (byTime.From is null || e.CreatedAt >= byTime.From) &&
                                      (byTime.Before is null || e.CreatedAt < byTime.Before),

            EventTypeFilter byType => byType.EventType.IsInstanceOfType(e.DecodedPayload) &&
                                      IsTrueOf(byType.Predicate, e.DecodedPayload),

            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };
    }

    // A predicate that cannot be evaluated because a member is null does not match the event.
    private static bool IsTrueOf(LambdaExpression? predicate, object e)
    {
        if (predicate is null)
            return true;

        try
        {
            var compiled = Predicates.GetOrAdd(predicate, x => x.Compile());
            return (bool)compiled.DynamicInvoke(e)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException)
        {
            return false;
        }
    }
}