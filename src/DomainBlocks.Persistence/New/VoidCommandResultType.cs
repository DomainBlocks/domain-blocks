using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public interface IVoidCommandResultType<TAggregate> : ICommandResultType
{
    public (TAggregate, IEnumerable<object>) GetUpdatedStateAndEvents(TAggregate aggregate);
}

public class VoidCommandResultType<TAggregate, TEventBase> : IVoidCommandResultType<TAggregate> where TEventBase : class
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

    (TAggregate, IEnumerable<object>) IVoidCommandResultType<TAggregate>.GetUpdatedStateAndEvents(TAggregate aggregate)
    {
        return GetUpdatedStateAndEvents(aggregate);
    }
}