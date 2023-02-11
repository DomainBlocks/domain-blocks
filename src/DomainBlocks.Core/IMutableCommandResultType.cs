namespace DomainBlocks.Core;

public interface IMutableCommandResultType<TAggregate, in TCommandResult> : ICommandResultType
{
    IReadOnlyCollection<object> SelectEventsAndUpdateStateIfRequired(
        TCommandResult commandResult, TAggregate state, Action<TAggregate, object> eventApplier);
}