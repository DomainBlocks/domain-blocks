using System;
using System.Collections.Generic;

namespace Shopping.Domain.New;

public abstract class AggregateBase
{
    private readonly List<object> _raisedEvents = new();

    public abstract Guid Id { get; }
    public IEnumerable<object> RaisedEvents => _raisedEvents.AsReadOnly();

    public void Apply(object @event)
    {
        ((dynamic)this).Apply((dynamic)@event);
    }

    protected void Raise<TEvent>(TEvent @event)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));
        Apply(@event);
        _raisedEvents.Add(@event);
    }
}