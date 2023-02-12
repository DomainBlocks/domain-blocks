namespace DomainBlocks.Core.Builders;

public sealed class MutableAggregateTypeBuilder<TAggregate, TEventBase> :
    AggregateTypeBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private readonly List<ICommandResultTypeBuilder> _commandResultTypeBuilders = new();
    private MutableAggregateType<TAggregate, TEventBase> _aggregateType = new();

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
            var commandResultType = new MutableEventEnumerableCommandResultType<TAggregate, TEventBase>();
            aggregateType = aggregateType.SetCommandResultType(commandResultType);

            return aggregateType;
        }
        set => _aggregateType = (MutableAggregateType<TAggregate, TEventBase>)value;
    }

    /// <summary>
    /// Specify an events selector for the aggregate. Use this option when the aggregate itself exposes the events it
    /// raises via a method or property. Any command results configured with <see cref="CommandResult{TCommandResult}"/>
    /// will be ignored when this option is used. 
    /// </summary>
    public void WithRaisedEventsFrom(Func<TAggregate, IReadOnlyCollection<TEventBase>> eventsSelector)
    {
        _aggregateType = _aggregateType.SetRaisedEventsSelector(eventsSelector);
    }

    /// <summary>
    /// Returns an object that can be used to configure a command result type. Use this option to configure how to
    /// access the raised events and updated state from returned command result objects.
    /// <returns>
    /// An object that can be used to configure the command result type.
    /// </returns>
    /// </summary>
    public MutableCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> CommandResult<TCommandResult>()
    {
        var builder = new MutableCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult>();
        _commandResultTypeBuilders.Add(builder);
        return builder;
    }

    /// <summary>
    /// Returns an object that can be used to configure command results of type <see cref="IEnumerable{T}"/>, where T
    /// is derived from <see cref="TEventBase"/>. Use this option when the aggregate exposes command methods which
    /// return an event enumerable representing the raised events.
    /// </summary>
    /// <returns>
    /// An object that can be used to configure event enumerable command results.
    /// </returns>
    public MutableEventEnumerableCommandResultTypeBuilder<TAggregate, TEventBase> WithEventEnumerableCommandResult()
    {
        var builder = new MutableEventEnumerableCommandResultTypeBuilder<TAggregate, TEventBase>();
        _commandResultTypeBuilders.Add(builder);
        return builder;
    }

    /// <summary>
    /// Specify an event applier for the aggregate. To arrive at the current state, the event applier is used to apply
    /// events to aggregate instances loaded from the event store. Events are also applied when commands are executed,
    /// if configured with command result type.
    /// </summary>
    public void ApplyEventsWith(Action<TAggregate, TEventBase> eventApplier)
    {
        _aggregateType = _aggregateType.SetEventApplier(eventApplier);
    }

    /// <summary>
    /// Adds the given event type to the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used to further configure the event.
    /// </returns>
    public IMutableAggregateEventTypeBuilder<TAggregate, TEvent> Event<TEvent>() where TEvent : TEventBase
    {
        var builder = new MutableAggregateEventTypeBuilder<TAggregate, TEventBase, TEvent>();
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
        var builder = AutoAggregateEventTypeBuilder<TAggregate, TEventBase>.Mutable();
        AutoEventTypeBuilder = builder;
        return builder;
    }
}