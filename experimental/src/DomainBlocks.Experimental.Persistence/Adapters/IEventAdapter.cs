namespace DomainBlocks.Experimental.Persistence.Adapters;

public interface IEventAdapter<in TReadEvent, out TWriteEvent, out TStreamVersion, TRawData> :
    IReadEventAdapter<TReadEvent, TRawData, TStreamVersion>,
    IWriteEventAdapter<TWriteEvent, TRawData>
    where TStreamVersion : struct
{
}