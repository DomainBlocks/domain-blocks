namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public interface IEventSourcedStateRepository
{
    (TState State, IStateEventStreamAppender Appender) New<TState>(string id);

    Task<(TState State, IStateEventStreamAppender Appender)> RestoreAsync<TState>(
        string id, CancellationToken cancellationToken = default);

    Task<TState> ReadOnlyRestoreAsync<TState>(string id, CancellationToken cancellationToken = default);
}