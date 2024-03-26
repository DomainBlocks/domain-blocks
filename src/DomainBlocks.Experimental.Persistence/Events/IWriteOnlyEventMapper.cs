namespace DomainBlocks.Experimental.Persistence.Events;

public interface IWriteOnlyEventMapper
{
    WriteEvent ToWriteEvent(object @event);
}