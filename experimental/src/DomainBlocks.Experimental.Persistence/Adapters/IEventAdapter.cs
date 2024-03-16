namespace DomainBlocks.Experimental.Persistence.Adapters;

public interface IEventAdapter<in TReadEvent, out TWriteEvent, out TStreamVersion> :
    IReadEventAdapter<TReadEvent, TStreamVersion>,
    IWriteEventAdapter<TWriteEvent>
    where TStreamVersion : struct
{
}