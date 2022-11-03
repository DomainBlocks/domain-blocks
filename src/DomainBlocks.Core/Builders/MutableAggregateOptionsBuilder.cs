using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public class MutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase> where TEventBase : class
{
    private MutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private MutableConventionalEventApplierBuilder<TAggregate, TEventBase> _eventApplierBuilder;

    protected override AggregateOptionsBase<TAggregate, TEventBase> Options
    {
        get
        {
            var options = (MutableAggregateOptions<TAggregate, TEventBase>)_options
                .WithCommandResultOptions(_commandResultOptionsBuilders.Select(x => x.Options));

            return _eventApplierBuilder == null ? options : options.WithEventApplier(_eventApplierBuilder.Build());
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

    public void ApplyEventsWith(Action<TAggregate, TEventBase> eventApplier)
    {
        _eventApplierBuilder = null;
        _options = _options.WithEventApplier(eventApplier);
    }

    public MutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention()
    {
        _eventApplierBuilder = new MutableConventionalEventApplierBuilder<TAggregate, TEventBase>();
        return _eventApplierBuilder;
    }
}