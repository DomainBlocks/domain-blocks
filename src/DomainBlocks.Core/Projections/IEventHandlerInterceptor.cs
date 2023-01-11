namespace DomainBlocks.Core.Projections;

public interface IEventHandlerInterceptor<in TState>
{
    Task Handle(
        IEventRecord eventRecord,
        TState state,
        Func<CancellationToken, Task> continuation,
        CancellationToken cancellationToken);
}