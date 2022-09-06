using System;
using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

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
        _builders = builders ?? throw new ArgumentNullException(nameof(builders));
        _eventApplierSource = eventApplierSource ?? throw new ArgumentNullException(nameof(eventApplierSource));
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
    private ApplyRaisedEventsBehavior _applyRaisedEventsBehavior = ApplyRaisedEventsBehavior.None;

    internal MutableCommandReturnTypeBuilder(IMutableEventApplierSource<TAggregate, TEventBase> eventApplierSource)
    {
        _eventApplierSource = eventApplierSource ?? throw new ArgumentNullException(nameof(eventApplierSource));
    }

    public IApplyRaisedEventsBehaviorBuilder WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _eventsSelector = eventsSelector ?? throw new ArgumentNullException(nameof(eventsSelector));
        return this;
    }
    
    void IApplyRaisedEventsBehaviorBuilder.ApplyEvents()
    {
        _applyRaisedEventsBehavior = ApplyRaisedEventsBehavior.ApplyAfterEnumerating;
    }

    void IApplyRaisedEventsBehaviorBuilder.ApplyEventsWhileEnumerating()
    {
        _applyRaisedEventsBehavior = ApplyRaisedEventsBehavior.ApplyWhileEnumerating;
    }

    ICommandReturnType ICommandReturnTypeBuilder.Build()
    {
        return new MutableCommandReturnType<TAggregate, TEventBase, TCommandResult>(
            _applyRaisedEventsBehavior,
            _eventsSelector,
            _eventApplierSource.EventApplier);
    }
}