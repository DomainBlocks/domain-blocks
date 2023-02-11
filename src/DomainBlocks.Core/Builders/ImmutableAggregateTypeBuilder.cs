namespace DomainBlocks.Core.Builders;

public sealed class ImmutableAggregateTypeBuilder<TAggregate, TEventBase> :
    AggregateTypeBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private readonly List<ICommandResultTypeBuilder> _commandResultTypeBuilders = new();
    private ImmutableAggregateType<TAggregate, TEventBase> _aggregateType = new();

    protected override AggregateTypeBase<TAggregate, TEventBase> AggregateTypeImpl
    {
        get
        {
            var commandResultTypes = _commandResultTypeBuilders.Select(x => x.CommandResultType);
            var aggregateType = _aggregateType.SetCommandResultTypes(commandResultTypes);

            if (aggregateType.HasCommandResultType<IEnumerable<TEventBase>>())
            {
                return aggregateType;
            }

            // We support IEnumerable<TEventBase> as a command result by default.
            var commandResultType = new ImmutableCommandResultType<TAggregate, TEventBase, IEnumerable<TEventBase>>()
                .SetEventsSelector(x => x);

            return aggregateType.SetCommandResultType(commandResultType);
        }
        set => _aggregateType = (ImmutableAggregateType<TAggregate, TEventBase>)value;
    }

    /// <summary>
    /// Returns an object that can be used to configure a command result type. Use this option to configure how to
    /// access the raised events and updated state from returned command result objects.
    /// <returns>
    /// An object that can be used to configure the command result type.
    /// </returns>
    /// </summary>
    public ImmutableCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> CommandResult<TCommandResult>()
    {
        var builder = new ImmutableCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult>();
        _commandResultTypeBuilders.Add(builder);
        return builder;
    }

    /// <summary>
    /// Specify an event applier for the aggregate. To arrive at the current state, the event applier is used to apply
    /// events to aggregate instances loaded from the event store. By default, events are also applied when commands
    /// are invoked.
    /// </summary>
    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        AggregateTypeImpl = _aggregateType.SetEventApplier(eventApplier);
    }

    /// <summary>
    /// Adds the given event type to the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used to further configure the event.
    /// </returns>
    public IImmutableAggregateEventTypeBuilder<TAggregate, TEvent> Event<TEvent>() where TEvent : TEventBase
    {
        var builder = new ImmutableAggregateEventTypeBuilder<TAggregate, TEventBase, TEvent>();
        EventTypeBuilders.Add(builder);
        return builder;
    }

    /// <summary>
    /// Automatically configure events by discovering event applier methods on the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public IAutoEventTypeBuilder AutoConfigureEvents()
    {
        var builder = AutoAggregateEventTypeBuilder<TAggregate, TEventBase>.Immutable();
        AutoEventTypeBuilder = builder;
        return builder;
    }

    /// <summary>
    /// Automatically configure events by discovering static, non-member event applier methods from a given type.
    /// Methods are expected have a signature with the following form:
    /// <code>static TAggregate Apply(TAggregate state, MyEvent e)</code>
    /// </summary>
    /// <param name="sourceType">The type to discover events and applier methods from.</param>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public IAutoEventTypeBuilder AutoConfigureEventsFrom(Type sourceType)
    {
        var builder = AutoAggregateEventTypeBuilder<TAggregate, TEventBase>.ImmutableNonMember(sourceType);
        AutoEventTypeBuilder = builder;
        return builder;
    }
}