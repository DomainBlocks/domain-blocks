namespace DomainBlocks.Core.Builders;

public sealed class ImmutableCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultTypeBuilder,
    IImmutableCommandResultUpdatedStateBuilder<TAggregate, TCommandResult>
    where TEventBase : class
{
    private ImmutableCommandResultType<TAggregate, TEventBase, TCommandResult> _commandResultType = new();

    ICommandResultType ICommandResultTypeBuilder.CommandResultType => _commandResultType;

    /// <summary>
    /// Specify where to select the events from in the command result object. The events are applied to arrived at the
    /// updated state for the immutable aggregate, unless <see cref="IImmutableCommandResultUpdatedStateBuilder{TAggregate,TCommandResult}.WithUpdatedStateFrom(Func{TCommandResult, TAggregate})"/>
    /// is used.
    /// </summary>
    /// <returns>
    /// An object that can be used to further configure the command result type.
    /// </returns>
    public IImmutableCommandResultUpdatedStateBuilder<TAggregate, TCommandResult> WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _commandResultType = _commandResultType.SetEventsSelector(eventsSelector);
        return this;
    }

    void IImmutableCommandResultUpdatedStateBuilder<TAggregate, TCommandResult>.WithUpdatedStateFrom(
        Func<TCommandResult, TAggregate> updatedStateSelector)
    {
        _commandResultType = _commandResultType.SetUpdatedStateSelector(updatedStateSelector);
    }
}