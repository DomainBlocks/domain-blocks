using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public class VoidCommandResultType<TAggregate, TEventBase> : ICommandResultType
{
    private readonly Func<TAggregate, TAggregate> _updatedStateSelector;
    private readonly Func<TAggregate, IEnumerable<TEventBase>> _eventsSelector;

    public VoidCommandResultType(
        Func<TAggregate, TAggregate> updatedStateSelector, Func<TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _updatedStateSelector = updatedStateSelector;
        _eventsSelector = eventsSelector;
    }

    public Type ClrType => typeof(void);
    
    public (TAggregate, IEnumerable<TEventBase>) GetUpdatedStateAndEvents(TAggregate aggregate)
    {
        var updatedState = _updatedStateSelector(aggregate);
        var events = _eventsSelector(aggregate);
        return (updatedState, events);
    }
}