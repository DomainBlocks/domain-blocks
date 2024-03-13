namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

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

    public static EventTypeMap ToEventTypeMap(this IEnumerable<EventTypeMapping> mappings) => new(mappings);
}