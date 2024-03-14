namespace DomainBlocks.Experimental.Persistence.Configuration;

public interface IEventTypeMappingBuilder
{
    EventTypeMappingBuilderKind Kind { get; }

    IEnumerable<EventTypeMapping> Build();
}