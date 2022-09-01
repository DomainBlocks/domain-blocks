using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New.Builders;

public class VoidCommandResultTypeBuilder<TAggregate, TEventBase> : ICommandResultTypeBuilder
{
    private Func<TAggregate, TAggregate> _updatedStateSelector;
    private Func<TAggregate, IEnumerable<TEventBase>> _eventsSelector;

    public VoidCommandResultTypeBuilder<TAggregate, TEventBase> WithUpdatedStateFrom(
        Func<TAggregate, TAggregate> updatedStateSelector)
    {
        _updatedStateSelector = updatedStateSelector;
        return this;
    }

    public VoidCommandResultTypeBuilder<TAggregate, TEventBase> WithEventsFrom(
        Func<TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _eventsSelector = eventsSelector;
        return this;
    }

    public VoidCommandResultType<TAggregate, TEventBase> Build()
    {
        return new VoidCommandResultType<TAggregate, TEventBase>(_updatedStateSelector, _eventsSelector);
    }

    ICommandResultType ICommandResultTypeBuilder.Build() => Build();
}