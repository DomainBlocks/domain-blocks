using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public sealed class ImmutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private ImmutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private ImmutableAutoEventApplierBuilder<TAggregate, TEventBase> _autoEventApplierBuilder;

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

            var eventApplier = _autoEventApplierBuilder?.Build();
            return eventApplier == null ? options : options.WithEventApplier(eventApplier);
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
    /// events to aggregate instances loaded from the event store. If configured to do so, events are also applied when
    /// commands are invoked.
    /// </summary>
    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _autoEventApplierBuilder = null;
        OptionsImpl = _options.WithEventApplier(eventApplier);
    }

    /// <summary>
    /// Auto-configures applying events by discovering event applier methods on the aggregate type. The event applier
    /// methods are discovered by reflection during configuration, and for performance reasons, are compiled into IL
    /// using lambda expressions.
    /// </summary>
    /// <returns>
    /// An object that can be used to configure the discovery of event applier methods.
    /// </returns>
    public ImmutableAutoEventApplierBuilder<TAggregate, TEventBase> DiscoverEventApplierMethods()
    {
        _autoEventApplierBuilder = new ImmutableAutoEventApplierBuilder<TAggregate, TEventBase>();
        return _autoEventApplierBuilder;
    }
}