using DomainBlocks.Experimental.Persistence.Events;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class EventTypeMappingEnumerableExtensions
{
    public static IEnumerable<EventTypeMapping> AddOrReplaceWith(
        this IEnumerable<EventTypeMapping> first, IEnumerable<EventTypeMapping> second)
    {
        return first
            .Concat(second)
            .GroupBy(x => x.EventType)
            .Select(x => x.Aggregate((_, next) => next));
    }
}