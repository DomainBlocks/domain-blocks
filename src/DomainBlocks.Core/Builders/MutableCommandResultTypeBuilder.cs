namespace DomainBlocks.Core.Builders;

public sealed class MutableCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultTypeBuilder, IMutableCommandResultTypeApplyEventsBuilder where TEventBase : class
{
    private MutableCommandResultType<TAggregate, TEventBase, TCommandResult> _commandResultType = new();

    ICommandResultType ICommandResultTypeBuilder.CommandResultType => _commandResultType;

    /// <summary>
    /// Specify where to select the events from in the command result object.
    /// </summary>
    /// <returns>
    /// An object that can be used to further configure the command result type.
    /// </returns>
    public IMutableCommandResultTypeApplyEventsBuilder WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _commandResultType = _commandResultType.SetEventsSelector(eventsSelector);
        return this;
    }

    void IMutableCommandResultTypeApplyEventsBuilder.ApplyEvents()
    {
        _commandResultType = _commandResultType.SetApplyEventsEnabled(true);
    }
}