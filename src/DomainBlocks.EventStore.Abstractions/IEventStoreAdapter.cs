namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreAdapter<TPayload> where TPayload : notnull
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<TPayload>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<CommittedEvent<TPayload>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}