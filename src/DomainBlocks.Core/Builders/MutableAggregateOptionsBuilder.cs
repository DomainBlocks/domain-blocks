using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public sealed class MutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private MutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private MutableAutoEventApplierBuilder<TAggregate, TEventBase> _autoEventApplierBuilder;

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

            var eventApplier = _autoEventApplierBuilder?.Build();
            
            return eventApplier == null
                ? options
                : ((MutableAggregateOptions<TAggregate, TEventBase>)options).WithEventApplier(eventApplier);
        }
        set => _options = (MutableAggregateOptions<TAggregate, TEventBase>)value;
    }

    /// <summary>
    /// Specify an events selector for the aggregate. Use this option when the aggregate itself exposes the events it
    /// raises via a method or property. Any command results configured with <see cref="CommandResult{TCommandResult}"/>
    /// will be ignored when this method is used. 
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
    /// events to aggregate instances loaded from the event store. If configured to do so, events are also applied when
    /// commands are invoked.
    /// </summary>
    public void ApplyEventsWith(Action<TAggregate, TEventBase> eventApplier)
    {
        _autoEventApplierBuilder = null;
        _options = _options.WithEventApplier(eventApplier);
    }

    /// <summary>
    /// Auto-configures applying events by discovering event applier methods on the aggregate type. The event applier
    /// methods are discovered by reflection during configuration, and for performance reasons, are compiled into IL
    /// using lambda expressions.
    /// </summary>
    /// <returns>
    /// An object that can be used to configure the discovery of event applier methods.
    /// </returns>
    public MutableAutoEventApplierBuilder<TAggregate, TEventBase> DiscoverEventApplierMethods()
    {
        _autoEventApplierBuilder = new MutableAutoEventApplierBuilder<TAggregate, TEventBase>();
        return _autoEventApplierBuilder;
    }
}