namespace DomainBlocks.Core.Subscriptions.Extensions;

internal static class EventStreamConsumerExtensions
{
    public static IEventStreamConsumer<TEvent, TPosition> Intercept<TEvent, TPosition>(
        this IEventStreamConsumer<TEvent, TPosition> consumer,
        IEnumerable<IEventStreamInterceptor<TEvent, TPosition>> interceptors)
        where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    {
        return interceptors
            .Reverse()
            .Aggregate(consumer, (acc, next) => acc.Intercept(next));
    }

    private static IEventStreamConsumer<TEvent, TPosition> Intercept<TEvent, TPosition>(
        this IEventStreamConsumer<TEvent, TPosition> consumer,
        IEventStreamInterceptor<TEvent, TPosition> interceptor)
        where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    {
        return new InterceptingEventStreamConsumer<TEvent, TPosition>(consumer, interceptor);
    }
}