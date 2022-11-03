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
    private readonly List<ICommandReturnTypeBuilder> _builders = new();

    internal IEnumerable<ICommandReturnType> Options => _builders.Select(x => x.Options);

    public MutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult>
        CommandReturnType<TCommandResult>()
    {
        var builder = new MutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult>();
        _builders.Add(builder);
        return builder;
    }
}

public class MutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandReturnTypeBuilder, IApplyRaisedEventsBehaviorBuilder where TEventBase : class
{
    private MutableCommandReturnType<TAggregate, TEventBase, TCommandResult> _options = new();

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