namespace DomainBlocks.Core.Builders;

public interface IImmutableCommandResultUpdatedStateBuilder<in TAggregate, out TCommandResult>
{
    /// <summary>
    /// Specify where to select the updated aggregate state from in the command result object. Use this option when the
    /// command result contains the updated state of the aggregate.
    /// </summary>
    public void WithUpdatedStateFrom(Func<TCommandResult, TAggregate> updatedStateSelector);
}