namespace DomainBlocks.Experimental.EventSourcing.Persistence;

/// <summary>
/// Exposes both read and write operations for a store of events.
/// </summary>
public interface IEventStore<out TReadEvent, in TWriteEvent, TStreamVersion> :
    IReadOnlyEventStore<TReadEvent, TStreamVersion>,
    IWriteOnlyEventStore<TWriteEvent, TStreamVersion>
    where TStreamVersion : struct
{
}