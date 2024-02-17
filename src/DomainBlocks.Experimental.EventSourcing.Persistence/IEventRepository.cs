namespace DomainBlocks.Experimental.EventSourcing.Persistence;

/// <summary>
/// Exposes both read and write operations for a repository of events.
/// </summary>
public interface IEventRepository<out TReadEvent, in TWriteEvent, TStreamVersion> :
    IReadOnlyEventRepository<TReadEvent, TStreamVersion>,
    IWriteOnlyEventRepository<TWriteEvent, TStreamVersion>
    where TStreamVersion : struct
{
}