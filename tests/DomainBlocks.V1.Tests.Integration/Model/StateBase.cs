namespace DomainBlocks.V1.Tests.Integration.Model;

public abstract record StateBase<T> where T : StateBase<T>, new()
{
    private static readonly Dictionary<Type, Func<T, object, T>> EventAppliers = new();

    public T Apply(object @event) => When(@event);

    protected static void When<TEvent>(Func<T, TEvent, T> eventApplier)
    {
        EventAppliers.Add(typeof(TEvent), (s, e) => eventApplier(s, (TEvent)e));
    }

    protected virtual T When(object @event)
    {
        var eventType = @event.GetType();

        if (EventAppliers.TryGetValue(eventType, out var eventApplier))
        {
            return eventApplier((T)this, @event);
        }

        return (T)this;
    }
}