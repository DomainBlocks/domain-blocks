using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Core.Subscriptions.Builders;

public class EventStreamConsumerBuilder<TEvent, TPosition> :
    IEventStreamConsumerBuilderInfrastructure<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly List<IEventStreamConsumerBuilder<TEvent, TPosition>> _subscriberBuilders = new();
    private readonly List<IEventStreamInterceptor<TEvent, TPosition>> _interceptors = new();

    public EventStreamConsumerBuilder(EventStreamSubscriptionBuilder coreBuilder)
    {
        CoreBuilder = coreBuilder;
    }

    public EventStreamSubscriptionBuilder CoreBuilder { get; }

    public EventStreamConsumerBuilder<TEvent, TPosition> AddInterceptors(
        IEnumerable<IEventStreamInterceptor<TEvent, TPosition>> interceptors)
    {
        _interceptors.AddRange(interceptors);
        return this;
    }

    public EventStreamConsumerBuilder<TEvent, TPosition> AddInterceptors(
        params IEventStreamInterceptor<TEvent, TPosition>[] interceptors)
    {
        _interceptors.AddRange(interceptors);
        return this;
    }

    void IEventStreamConsumerBuilderInfrastructure<TEvent, TPosition>.AddConsumerBuilder(
        IEventStreamConsumerBuilder<TEvent, TPosition> builder)
    {
        _subscriberBuilders.Add(builder);
    }

    IEnumerable<IEventStreamConsumer<TEvent, TPosition>>
        IEventStreamConsumerBuilderInfrastructure<TEvent, TPosition>.Build(IReadEventAdapter<TEvent> readEventAdapter)
    {
        var subscribers = _subscriberBuilders.Select(x => x.Build(readEventAdapter).Intercept(_interceptors));
        return subscribers;
    }
}