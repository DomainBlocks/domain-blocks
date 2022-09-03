using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New.Builders;

public interface IVoidCommandResultUpdatedStateSelectorBuilder<TAggregate>
{
    public void WithUpdatedStateFrom(Func<TAggregate, TAggregate> updatedStateSelector);
}

public class VoidCommandResultTypeBuilder<TAggregate, TEventBase> :
    ICommandResultTypeBuilder,
    IVoidCommandResultUpdatedStateSelectorBuilder<TAggregate>
    where TEventBase : class
{
    private Func<TAggregate, TAggregate> _updatedStateSelector;
    private Func<TAggregate, IEnumerable<TEventBase>> _eventsSelector;

    internal VoidCommandResultTypeBuilder()
    {
    }

    public IVoidCommandResultUpdatedStateSelectorBuilder<TAggregate> WithEventsFrom(
        Func<TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _eventsSelector = eventsSelector;
        return this;
    }

    void IVoidCommandResultUpdatedStateSelectorBuilder<TAggregate>.WithUpdatedStateFrom(
        Func<TAggregate, TAggregate> updatedStateSelector)
    {
        _updatedStateSelector = updatedStateSelector;
    }

    ICommandResultType ICommandResultTypeBuilder.Build()
    {
        return new VoidCommandResultType<TAggregate, TEventBase>(_updatedStateSelector, _eventsSelector);
    }
}