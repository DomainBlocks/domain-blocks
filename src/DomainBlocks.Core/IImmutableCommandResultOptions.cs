namespace DomainBlocks.Core;

public interface IImmutableCommandResultOptions<TAggregate, in TCommandResult> : ICommandResultOptions
{
    IReadOnlyCollection<object> SelectEventsAndUpdateState(
        TCommandResult commandResult, ref TAggregate state, Func<TAggregate, object, TAggregate> eventApplier);
}