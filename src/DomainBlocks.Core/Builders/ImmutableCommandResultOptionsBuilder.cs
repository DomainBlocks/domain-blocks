namespace DomainBlocks.Core.Builders;

public interface IImmutableCommandResultUpdatedStateBuilder<in TAggregate, out TCommandResult>
{
    /// <summary>
    /// Specify where to select the updated aggregate state from in the command result object. Use this option when the
    /// command result contains the updated state of the aggregate.
    /// </summary>
    public void WithUpdatedStateFrom(Func<TCommandResult, TAggregate> updatedStateSelector);
}

public sealed class ImmutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultOptionsBuilder,
    IImmutableCommandResultUpdatedStateBuilder<TAggregate, TCommandResult>
    where TEventBase : class
{
    private ImmutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> _options = new();

    ICommandResultOptions ICommandResultOptionsBuilder.Options => _options;

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
        _options = _options.WithEventsSelector(eventsSelector);
        return this;
    }

    void IImmutableCommandResultUpdatedStateBuilder<TAggregate, TCommandResult>.WithUpdatedStateFrom(
        Func<TCommandResult, TAggregate> updatedStateSelector)
    {
        _options = _options.WithUpdatedStateSelector(updatedStateSelector);
    }
}