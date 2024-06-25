using System.Diagnostics;
using System.Linq.Expressions;
using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public static class EventStreamConsumerExtensions
{
    public static Dictionary<Type, Func<EventHandlerContext, Task>> GetEventHandlers(this IEventStreamConsumer consumer)
    {
        return consumer
            .GetType()
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
            .Select(i => GetEventHandler(i, consumer))
            .ToDictionary(x => x.EventType, x => x.Handler);
    }

    private static (Type EventType, Func<EventHandlerContext, Task> Handler) GetEventHandler(
        Type eventHandlerInterfaceType, IEventStreamConsumer consumer)
    {
        var openConvertMethod = typeof(EventHandlerContext).GetMethod(nameof(EventHandlerContext.Convert));
        Debug.Assert(openConvertMethod != null, nameof(openConvertMethod) + " != null");

        var eventType = eventHandlerInterfaceType.GetGenericArguments()[0];
        var convertMethod = openConvertMethod.MakeGenericMethod(eventType);

        var contextParam = Expression.Parameter(typeof(EventHandlerContext), "context");
        var convertContextCall = Expression.Call(contextParam, convertMethod);

        var onEventAsyncMethod = eventHandlerInterfaceType.GetMethod(nameof(IEventHandler<object>.OnEventAsync));
        Debug.Assert(onEventAsyncMethod != null, nameof(onEventAsyncMethod) + " != null");

        var instance = Expression.Constant(consumer);
        var onEventAsyncCall = Expression.Call(instance, onEventAsyncMethod, convertContextCall);
        var lambda = Expression.Lambda<Func<EventHandlerContext, Task>>(onEventAsyncCall, contextParam);
        var compiledLambda = lambda.Compile();

        return (eventType, compiledLambda);
    }
}