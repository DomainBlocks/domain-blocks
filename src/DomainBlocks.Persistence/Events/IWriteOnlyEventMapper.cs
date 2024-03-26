namespace DomainBlocks.Persistence.Events;

public interface IWriteOnlyEventMapper
{
    WriteEvent ToWriteEvent(object @event);
}