namespace DomainBlocks.Testing.Integration;

public interface ITestEventStoreFactory<TEvent, TStreamId, TStreamPos, TLogPos> :
    IAsyncDisposable
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    Task<ITestEventStoreHandle<TEvent, TStreamId, TStreamPos, TLogPos>> CreateAsync(
        string name,
        CancellationToken cancellationToken = default);
}