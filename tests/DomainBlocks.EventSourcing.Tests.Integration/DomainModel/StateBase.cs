using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;

namespace DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

public abstract record StateBase<T> where T : StateBase<T>, new()
{
    private static readonly Dictionary<Type, Func<T, IDomainEvent, T>> EventAppliers = [];

    public T Apply(IDomainEvent @event) => When(@event);

    protected static void When<TEvent>(Func<T, TEvent, T> eventApplier)
    {
        EventAppliers.Add(typeof(TEvent), (s, e) => eventApplier(s, (TEvent)e));
    }

    protected virtual T When(IDomainEvent @event)
    {
        var eventType = @event.GetType();

        if (EventAppliers.TryGetValue(eventType, out var eventApplier))
            return eventApplier((T)this, @event);

        return (T)this;
    }
}