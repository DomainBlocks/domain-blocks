namespace DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;

public interface IReadEventAdapter<in TReadEvent, TRawData, out TStreamVersion> where TStreamVersion : struct
{
    string GetEventName(TReadEvent readEvent);
    Task<TRawData> GetEventData(TReadEvent readEvent);
    bool TryGetMetadata(TReadEvent readEvent, out TRawData? metadata);
    TStreamVersion GetStreamVersion(TReadEvent readEvent);
}