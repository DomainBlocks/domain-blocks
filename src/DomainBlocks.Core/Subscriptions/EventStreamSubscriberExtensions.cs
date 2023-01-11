namespace DomainBlocks.Core.Subscriptions;

internal static class EventStreamSubscriberExtensions
{
    public static IEventStreamSubscriber<TEvent, TPosition> Intercept<TEvent, TPosition>(
        this IEventStreamSubscriber<TEvent, TPosition> subscriber,
        IEnumerable<IEventStreamSubscriberInterceptor<TEvent, TPosition>> interceptors)
        where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    {
        return interceptors
            .Reverse()
            .Aggregate(subscriber, (acc, next) => acc.Intercept(next));
    }

    private static IEventStreamSubscriber<TEvent, TPosition> Intercept<TEvent, TPosition>(
        this IEventStreamSubscriber<TEvent, TPosition> subscriber,
        IEventStreamSubscriberInterceptor<TEvent, TPosition> interceptor)
        where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    {
        return new InterceptingEventStreamSubscriber<TEvent, TPosition>(subscriber, interceptor);
    }
}