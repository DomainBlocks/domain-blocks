using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public sealed class ImmutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private readonly List<IEventOptionsBuilder<TAggregate>> _eventOptionsBuilders = new();
    private ImmutableAggregateOptions<TAggregate, TEventBase> _options = new();

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
        // TODO: Do we want to keep this?
        OptionsImpl = _options.WithEventApplier(eventApplier);
    }

    public ImmutableEventOptionsBuilder<TAggregate, TEvent> Event<TEvent>()
    {
        var builder = new ImmutableEventOptionsBuilder<TAggregate, TEvent>();
        _eventOptionsBuilders.Add(builder);
        return builder;
    }
}