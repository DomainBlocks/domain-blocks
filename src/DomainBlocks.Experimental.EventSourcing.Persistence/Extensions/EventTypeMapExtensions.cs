namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

public static class EventTypeMapExtensions
{
    public static ExtendedEventTypeMap<TState> Extend<TState>(this EventTypeMap<TState> map)
    {
        var extendedMappings = map.Select(x => x.Extend());
        return new ExtendedEventTypeMap<TState>(extendedMappings);
    }
}