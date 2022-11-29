using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public sealed class ImmutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private ImmutableAggregateOptions<TAggregate, TEventBase> _options = new();

    protected override AggregateOptionsBase<TAggregate, TEventBase> OptionsImpl
    {
        get
        {
            var commandResultsOptions = _commandResultOptionsBuilders.Select(x => x.Options);
            var options = _options.WithCommandResultsOptions(commandResultsOptions);

            if (options.HasCommandResultOptions<IEnumerable<TEventBase>>())
            {
                return options;
            }

            // We support IEnumerable<TEventBase> as a command result by default.
            var commandResultOptions =
                new ImmutableCommandResultOptions<TAggregate, TEventBase, IEnumerable<TEventBase>>()
                    .WithEventsSelector(x => x);

            return options.WithCommandResultOptions(commandResultOptions);
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
        EventOptionsBuilders.Add(builder);
        return builder;
    }

    /// <summary>
    /// Automatically configure events by discovering event applier methods on the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public IAutoEventOptionsBuilder AutoConfigureEvents()
    {
        var builder = AutoEventOptionsBuilder<TAggregate, TEventBase>.Immutable();
        AutoEventOptionsBuilder = builder;
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
    public IAutoEventOptionsBuilder AutoConfigureEventsFrom(Type sourceType)
    {
        var builder = AutoEventOptionsBuilder<TAggregate, TEventBase>.ImmutableNonMember(sourceType);
        AutoEventOptionsBuilder = builder;
        return builder;
    }
}