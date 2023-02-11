namespace DomainBlocks.Core;

public interface IImmutableCommandResultType<TAggregate, in TCommandResult> : ICommandResultType
{
    IReadOnlyCollection<object> SelectEventsAndUpdateStateIfRequired(
        TCommandResult commandResult, ref TAggregate state, Func<TAggregate, object, TAggregate> eventApplier);
}