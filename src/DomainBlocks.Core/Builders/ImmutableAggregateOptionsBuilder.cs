using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public sealed class ImmutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private readonly List<IEventOptionsBuilder<TAggregate, TEventBase>> _eventOptionsBuilders = new();
    private ImmutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private ImmutableAutoEventOptionsBuilder<TAggregate, TEventBase> _autoEventOptionsBuilder;

    protected override AggregateOptionsBase<TAggregate, TEventBase> OptionsImpl
    {
        get
        {
            var commandResultsOptions = _commandResultOptionsBuilders.Select(x => x.Options);
            var options = _options.WithCommandResultsOptions(commandResultsOptions);

            if (!options.HasCommandResultOptions<IEnumerable<TEventBase>>())
            {
                // We support IEnumerable<TEventBase> as a command result by default.
                var commandResultOptions =
                    new ImmutableCommandResultOptions<TAggregate, TEventBase, IEnumerable<TEventBase>>()
                        .WithEventsSelector(x => x);

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
        set => _options = (ImmutableAggregateOptions<TAggregate, TEventBase>)value;
    }

    /// <summary>
    /// Returns an object that can be used to configure a command result type. Use this option to configure how to
    /// access the raised events and updated state from returned command result objects.
    /// <returns>
    /// An object that can be used to configure the command result type.
    /// </returns>
    /// </summary>
    public ImmutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> CommandResult<TCommandResult>()
    {
        var builder = new ImmutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult>();
        _commandResultOptionsBuilders.Add(builder);
        return builder;
    }

    /// <summary>
    /// Specify an event applier for the aggregate. To arrive at the current state, the event applier is used to apply
    /// events to aggregate instances loaded from the event store. By default, events are also applied when commands
    /// are invoked.
    /// </summary>
    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        OptionsImpl = _options.WithEventApplier(eventApplier);
    }

    /// <summary>
    /// Adds the given event type to the aggregate options.
    /// </summary>
    /// <returns>
    /// An object that can be used to further configure the event.
    /// </returns>
    public ImmutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> Event<TEvent>() where TEvent : TEventBase
    {
        var builder = new ImmutableEventOptionsBuilder<TAggregate, TEventBase, TEvent>();
        _eventOptionsBuilders.Add(builder);
        return builder;
    }

    /// <summary>
    /// Automatically configure events by discovering event applier methods on the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public ImmutableAutoEventOptionsBuilder<TAggregate, TEventBase> AutoConfigureEventsFromApplyMethods()
    {
        _autoEventOptionsBuilder = new ImmutableAutoEventOptionsBuilder<TAggregate, TEventBase>();
        return _autoEventOptionsBuilder;
    }
}