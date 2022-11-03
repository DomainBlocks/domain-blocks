using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public interface IImmutableRaisedEventsBuilder<TAggregate, TEventBase> where TEventBase : class
{
    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier);
    public ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention();
}

public class ImmutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase>,
    IImmutableRaisedEventsBuilder<TAggregate, TEventBase>
    where TEventBase : class
{
    private ImmutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private readonly List<ICommandResultOptionsBuilder> _commandResultOptionsBuilders = new();
    private ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> _eventApplierBuilder;

    protected override AggregateOptionsBase<TAggregate, TEventBase> Options
    {
        get
        {
            var options = _options.WithCommandResultOptions(_commandResultOptionsBuilders.Select(x => x.Options));
            return _eventApplierBuilder == null ? options : options.WithEventApplier(_eventApplierBuilder.Build());
        }
        set => _options = (ImmutableAggregateOptions<TAggregate, TEventBase>)value;
    }

    public ImmutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> CommandResult<TCommandResult>()
    {
        var builder = new ImmutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult>();
        _commandResultOptionsBuilders.Add(builder);
        return builder;
    }

    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _eventApplierBuilder = null;
        Options = _options.WithEventApplier(eventApplier);
    }

    public ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention()
    {
        _eventApplierBuilder = new ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase>();
        return _eventApplierBuilder;
    }
}