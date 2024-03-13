namespace DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

public interface IEventTypeMappingBuilder
{
    EventTypeMappingBuilderKind Kind { get; }

    IEnumerable<EventTypeMapping> Build();
}