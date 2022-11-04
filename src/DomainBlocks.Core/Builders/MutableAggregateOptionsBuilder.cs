using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public class MutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private MutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private MutableReflectionEventApplierBuilder<TAggregate, TEventBase> _reflectionEventApplierBuilder;

    protected override AggregateOptionsBase<TAggregate, TEventBase> Options
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

            var eventApplier = _reflectionEventApplierBuilder?.Build();
            
            return eventApplier == null
                ? options
                : ((MutableAggregateOptions<TAggregate, TEventBase>)options).WithEventApplier(eventApplier);
        }
        set => _options = (MutableAggregateOptions<TAggregate, TEventBase>)value;
    }

    public void WithRaisedEventsFrom(Func<TAggregate, IReadOnlyCollection<TEventBase>> eventsSelector)
    {
        _options = _options.WithRaisedEventsSelector(eventsSelector);
    }

    public MutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> CommandResult<TCommandResult>()
    {
        var builder = new MutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult>();
        _commandResultOptionsBuilders.Add(builder);
        return builder;
    }

    public void WithEventEnumerableCommandResult(EventEnumerationMode mode)
    {
        var commandResultOptions = new MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase>()
            .WithEventEnumerationMode(mode);

        Options = _options.WithCommandResultOptions(commandResultOptions);
    }

    public void ApplyEventsWith(Action<TAggregate, TEventBase> eventApplier)
    {
        _reflectionEventApplierBuilder = null;
        _options = _options.WithEventApplier(eventApplier);
    }

    public MutableReflectionEventApplierBuilder<TAggregate, TEventBase> DiscoverEventApplierMethods()
    {
        _reflectionEventApplierBuilder = new MutableReflectionEventApplierBuilder<TAggregate, TEventBase>();
        return _reflectionEventApplierBuilder;
    }
}