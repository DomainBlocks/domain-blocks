namespace DomainBlocks.Core.Projections.Internal;

internal interface IEventHandlerInvoker<in TState>
{
    Task Invoke(IEventRecord eventRecord, TState state, CancellationToken cancellationToken);
}