namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public interface IEventAdapter<in TReadEvent, out TWriteEvent, TRawData, out TStreamVersion> :
    IReadEventAdapter<TReadEvent, TRawData, TStreamVersion>,
    IWriteEventAdapter<TWriteEvent, TRawData>
    where TStreamVersion : struct
{
}