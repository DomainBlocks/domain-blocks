namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public interface IReadEventAdapter<in TReadEvent, TRawData, out TStreamVersion> where TStreamVersion : struct
{
    string GetEventName(TReadEvent readEvent);
    Task<TRawData> GetEventData(TReadEvent readEvent);
    TRawData? GetEventMetadata(TReadEvent readEvent);
    TStreamVersion GetStreamVersion(TReadEvent readEvent);
}