namespace DomainBlocks.Experimental.Persistence.Events;

public interface IEventMapper : IReadOnlyEventMapper
{
    WriteEvent ToWriteEvent(object @event);
}