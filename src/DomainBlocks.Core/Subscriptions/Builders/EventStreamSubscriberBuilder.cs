using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Core.Subscriptions.Builders;

public class EventStreamSubscriberBuilder<TEvent, TPosition> :
    IEventStreamSubscriberBuilderInfrastructure<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly List<IEventStreamSubscriberBuilder<TEvent, TPosition>> _subscriberBuilders = new();
    private readonly List<IEventStreamSubscriberInterceptor<TEvent, TPosition>> _interceptors = new();

    public EventStreamSubscriberBuilder(EventStreamSubscriptionBuilder coreBuilder)
    {
        CoreBuilder = coreBuilder;
    }

    public EventStreamSubscriptionBuilder CoreBuilder { get; }

    public EventStreamSubscriberBuilder<TEvent, TPosition> AddInterceptors(
        IEnumerable<IEventStreamSubscriberInterceptor<TEvent, TPosition>> interceptors)
    {
        _interceptors.AddRange(interceptors);
        return this;
    }

    public EventStreamSubscriberBuilder<TEvent, TPosition> AddInterceptors(
        params IEventStreamSubscriberInterceptor<TEvent, TPosition>[] interceptors)
    {
        _interceptors.AddRange(interceptors);
        return this;
    }

    void IEventStreamSubscriberBuilderInfrastructure<TEvent, TPosition>.AddSubscriberBuilder(
        IEventStreamSubscriberBuilder<TEvent, TPosition> builder)
    {
        _subscriberBuilders.Add(builder);
    }

    IEventStreamSubscriber<TEvent, TPosition> IEventStreamSubscriberBuilderInfrastructure<TEvent, TPosition>.Build(
        IReadEventAdapter<TEvent> readEventAdapter)
    {
        var subscribers = _subscriberBuilders.Select(x => x.Build(readEventAdapter)).ToList();

        return subscribers.Count switch
        {
            0 => throw new InvalidOperationException("Expected at least one subscriber"),
            1 => subscribers.First().Intercept(_interceptors),
            _ => new CompositeEventStreamSubscriber<TEvent, TPosition>(subscribers).Intercept(_interceptors)
        };
    }
}