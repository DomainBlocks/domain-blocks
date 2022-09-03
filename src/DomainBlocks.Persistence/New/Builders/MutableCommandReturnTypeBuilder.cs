using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New.Builders;

public interface IApplyRaisedEventsBehaviorBuilder
{
    public void ApplyEvents();
    public void ApplyEventsWhileEnumerating();
}

public class MutableCommandReturnTypeBuilder<TAggregate, TEventBase> where TEventBase : class
{
    private readonly List<ICommandReturnTypeBuilder> _builders;
    private readonly IMutableEventApplierSource<TAggregate, TEventBase> _eventApplierSource;

    internal MutableCommandReturnTypeBuilder(
        List<ICommandReturnTypeBuilder> builders, IMutableEventApplierSource<TAggregate, TEventBase> eventApplierSource)
    {
        _builders = builders;
        _eventApplierSource = eventApplierSource;
    }

    public MutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult> CommandReturnType<TCommandResult>()
    {
        var builder = new MutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult>(_eventApplierSource);
        _builders.Add(builder);
        return builder;
    }
}

public class MutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandReturnTypeBuilder,
    IApplyRaisedEventsBehaviorBuilder
    where TEventBase : class
{
    private readonly IMutableEventApplierSource<TAggregate, TEventBase> _eventApplierSource;
    private Func<TCommandResult, IEnumerable<TEventBase>> _eventsSelector;
    private ApplyEventsBehavior _applyEventsBehavior = ApplyEventsBehavior.None;

    internal MutableCommandReturnTypeBuilder(IMutableEventApplierSource<TAggregate, TEventBase> eventApplierSource)
    {
        _eventApplierSource = eventApplierSource;
    }

    public IApplyRaisedEventsBehaviorBuilder WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _eventsSelector = eventsSelector;
        return this;
    }
    
    void IApplyRaisedEventsBehaviorBuilder.ApplyEvents()
    {
        _applyEventsBehavior = ApplyEventsBehavior.ApplyAfterEnumerating;
    }

    void IApplyRaisedEventsBehaviorBuilder.ApplyEventsWhileEnumerating()
    {
        _applyEventsBehavior = ApplyEventsBehavior.ApplyWhileEnumerating;
    }

    ICommandReturnType ICommandReturnTypeBuilder.Build()
    {
        return new MutableCommandReturnType<TAggregate, TEventBase, TCommandResult>(
            _applyEventsBehavior,
            _eventsSelector,
            _eventApplierSource.EventApplier);
    }
}