using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections.Internal;

internal static class EventHandlerInvokerExtensions
{
    public static IEventHandlerInvoker<TState> Intercept<TState>(
        this IEventHandlerInvoker<TState> invoker, IEnumerable<IEventHandlerInterceptor<TState>> interceptors)
    {
        return interceptors
            .Reverse()
            .Aggregate(invoker, (acc, next) => acc.Intercept(next));
    }

    private static IEventHandlerInvoker<TState> Intercept<TState>(
        this IEventHandlerInvoker<TState> invoker, IEventHandlerInterceptor<TState> interceptor)
    {
        return new InterceptingEventHandlerInvoker<TState>(invoker, interceptor);
    }
}