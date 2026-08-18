namespace DomainBlocks.EventStore.Abstractions.New;

public interface IEventReader<out TEvent, in TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    IAsyncEnumerable<TEvent> Read(ReadDefinition<TLogPos> definition);

    IAsyncEnumerable<TEvent> Read(TStreamId streamId, ReadDefinition<TStreamPos> definition);
}