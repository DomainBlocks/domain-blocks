namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public interface IStateEventStreamAppender
{
    void Append(IEnumerable<object> events);
    Task CommitAsync(CancellationToken cancellationToken = default);
}