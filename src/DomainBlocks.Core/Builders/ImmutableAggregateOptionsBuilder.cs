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
    private ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> _conventionalEventApplierBuilder;

    protected override AggregateOptionsBase<TAggregate, TEventBase> Options
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

            var eventApplier = _conventionalEventApplierBuilder?.Build();
            return eventApplier == null ? options : options.WithEventApplier(eventApplier);
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
        _conventionalEventApplierBuilder = null;
        Options = _options.WithEventApplier(eventApplier);
    }

    public ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention()
    {
        _conventionalEventApplierBuilder = new ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase>();
        return _conventionalEventApplierBuilder;
    }
}