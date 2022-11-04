using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public class MutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private MutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private MutableConventionalEventApplierBuilder<TAggregate, TEventBase> _conventionalEventApplierBuilder;

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

            var eventApplier = _conventionalEventApplierBuilder?.Build();
            
            return eventApplier == null
                ? options
                : ((MutableAggregateOptions<TAggregate, TEventBase>)options).WithEventApplier(eventApplier);
        }
        set => _options = (MutableAggregateOptions<TAggregate, TEventBase>)value;
    }

    public void WithRaisedEventsFrom(Func<TAggregate, IEnumerable<TEventBase>> eventsSelector)
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
        _conventionalEventApplierBuilder = null;
        _options = _options.WithEventApplier(eventApplier);
    }

    public MutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention()
    {
        _conventionalEventApplierBuilder = new MutableConventionalEventApplierBuilder<TAggregate, TEventBase>();
        return _conventionalEventApplierBuilder;
    }
}