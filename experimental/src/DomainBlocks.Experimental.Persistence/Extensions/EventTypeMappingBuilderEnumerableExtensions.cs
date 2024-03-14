using DomainBlocks.Experimental.Persistence.Configuration;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class EventTypeMappingBuilderEnumerableExtensions
{
    public static EventTypeMap BuildEventTypeMap(this IReadOnlyCollection<IEventTypeMappingBuilder> builders)
    {
        // Include mappings from event base type builders first so they can be overriden.
        var eventBaseTypeMappings = builders
            .Where(x => x.Kind == EventTypeMappingBuilderKind.EventBaseType)
            .SelectMany(x => x.Build());

        var singleEventTypeMappings = builders
            .Where(x => x.Kind == EventTypeMappingBuilderKind.SingleEvent)
            .SelectMany(x => x.Build());

        return eventBaseTypeMappings.AddOrReplaceWith(singleEventTypeMappings).ToEventTypeMap();
    }
}