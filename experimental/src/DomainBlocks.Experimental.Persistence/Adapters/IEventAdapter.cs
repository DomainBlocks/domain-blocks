namespace DomainBlocks.Experimental.Persistence.Adapters;

public interface IEventAdapter<in TReadEvent, out TWriteEvent> :
    IReadEventAdapter<TReadEvent>,
    IWriteEventAdapter<TWriteEvent>
{
}