using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public sealed class MutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private readonly List<IEventOptionsBuilder<TAggregate, TEventBase>> _eventOptionsBuilders = new();
    private MutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private MutableAutoEventOptionsBuilder<TAggregate, TEventBase> _autoEventOptionsBuilder;

    protected override AggregateOptionsBase<TAggregate, TEventBase> OptionsImpl
    {
        get
        {
            var commandResultsOptions = _commandResultOptionsBuilders.Select(x => x.Options);
            var options = _options.WithCommandResultsOptions(commandResultsOptions);

            if (!options.HasCommandResultOptions<IEnumerable<TEventBase>>())
            {
                // We support IEnumerable<TEventBase> as a command result by default.
                var commandResultOptions = new MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase>();
                options = options.WithCommandResultOptions(commandResultOptions);
            }

            if (_autoEventOptionsBuilder != null)
            {
                options = options.WithEventsOptions(_autoEventOptionsBuilder.Build());
            }

            // Any individually configured event options will override corresponding auto configured event options.
            var eventsOptions = _eventOptionsBuilders.Select(x => x.Options);
            options = options.WithEventsOptions(eventsOptions);

            return options;
        }
        set => _options = (MutableAggregateOptions<TAggregate, TEventBase>)value;
    }

    /// <summary>
    /// Specify an events selector for the aggregate. Use this option when the aggregate itself exposes the events it
    /// raises via a method or property. Any command results configured with <see cref="CommandResult{TCommandResult}"/>
    /// will be ignored when this option is used. 
    /// </summary>
    public void WithRaisedEventsFrom(Func<TAggregate, IReadOnlyCollection<TEventBase>> eventsSelector)
    {
        _options = _options.WithRaisedEventsSelector(eventsSelector);
    }

    /// <summary>
    /// Returns an object that can be used to configure a command result type. Use this option to configure how to
    /// access the raised events and updated state from returned command result objects.
    /// <returns>
    /// An object that can be used to configure the command result type.
    /// </returns>
    /// </summary>
    public MutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> CommandResult<TCommandResult>()
    {
        var builder = new MutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult>();
        _commandResultOptionsBuilders.Add(builder);
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
    public MutableEventEnumerableCommandResultOptionsBuilder<TAggregate, TEventBase> WithEventEnumerableCommandResult()
    {
        var builder = new MutableEventEnumerableCommandResultOptionsBuilder<TAggregate, TEventBase>();
        _commandResultOptionsBuilders.Add(builder);
        return builder;
    }

    /// <summary>
    /// Specify an event applier for the aggregate. To arrive at the current state, the event applier is used to apply
    /// events to aggregate instances loaded from the event store. Events are also applied when commands are executed,
    /// if configured with command result options.
    /// </summary>
    public void ApplyEventsWith(Action<TAggregate, TEventBase> eventApplier)
    {
        _options = _options.WithEventApplier(eventApplier);
    }

    /// <summary>
    /// Adds the given event type to the aggregate options.
    /// </summary>
    /// <returns>
    /// An object that can be used to further configure the event.
    /// </returns>
    public MutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> Event<TEvent>() where TEvent : TEventBase
    {
        var builder = new MutableEventOptionsBuilder<TAggregate, TEventBase, TEvent>();
        _eventOptionsBuilders.Add(builder);
        return builder;
    }

    /// <summary>
    /// Automatically configure events by discovering event applier methods on the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public MutableAutoEventOptionsBuilder<TAggregate, TEventBase> AutoConfigureEventsFromApplyMethods()
    {
        _autoEventOptionsBuilder = new MutableAutoEventOptionsBuilder<TAggregate, TEventBase>();
        return _autoEventOptionsBuilder;
    }
}