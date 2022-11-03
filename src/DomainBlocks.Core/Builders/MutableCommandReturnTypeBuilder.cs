using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public interface IApplyRaisedEventsBehaviorBuilder
{
    public void ApplyEvents();
    public void ApplyEventsWhileEnumerating();
}

public class MutableCommandReturnTypeBuilder<TAggregate, TEventBase> where TEventBase : class
{
    private readonly IMutableEventApplierSource<TAggregate, TEventBase> _eventApplierSource;
    private readonly List<ICommandReturnTypeBuilder> _builders = new();

    internal MutableCommandReturnTypeBuilder(IMutableEventApplierSource<TAggregate, TEventBase> eventApplierSource)
    {
        _eventApplierSource = eventApplierSource;
    }
    
    internal IEnumerable<ICommandReturnType> Options => _builders.Select(x => x.Options);

    public MutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult>
        CommandReturnType<TCommandResult>()
    {
        var builder =
            new MutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult>(_eventApplierSource);
        _builders.Add(builder);
        return builder;
    }
}

public class MutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandReturnTypeBuilder,
    IApplyRaisedEventsBehaviorBuilder
    where TEventBase : class
{
    private MutableCommandReturnType<TAggregate, TEventBase, TCommandResult> _options = new();

    internal MutableCommandReturnTypeBuilder(
        IMutableEventApplierSource<TAggregate, TEventBase> eventApplierSource)
    {
        // TODO: get rid of this
        _options = _options.WithEventApplier(eventApplierSource.EventApplier);
    }

    public ICommandReturnType Options => _options;

    public IApplyRaisedEventsBehaviorBuilder WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _options = _options.WithEventsSelector(eventsSelector);
        return this;
    }
    
    void IApplyRaisedEventsBehaviorBuilder.ApplyEvents()
    {
        _options = _options.WithApplyRaisedEventsBehavior(ApplyRaisedEventsBehavior.ApplyAfterEnumerating);
    }

    void IApplyRaisedEventsBehaviorBuilder.ApplyEventsWhileEnumerating()
    {
        _options = _options.WithApplyRaisedEventsBehavior(ApplyRaisedEventsBehavior.ApplyWhileEnumerating);
    }
}