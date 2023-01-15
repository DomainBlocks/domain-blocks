namespace DomainBlocks.Core.Process;

public interface IProcessType<TProcess> : IEntityType<TProcess>
{
    ProcessTransitionResult<TProcess> InvokeTransition(TProcess process, object @event);
}