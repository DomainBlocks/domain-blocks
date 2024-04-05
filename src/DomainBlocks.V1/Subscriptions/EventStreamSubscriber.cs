using System.Diagnostics;
using System.Linq.Expressions;
using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamSubscriber
{
    private readonly Func<SubscriptionPosition?, IEventStreamSubscription> _subscriptionFactory;
    private readonly bool _isAllEventStream;
    private readonly IEventStreamConsumer _consumer;
    private readonly EventMapper _eventMapper;
    private readonly Dictionary<Type, Func<EventHandlerContext, Task>> _eventHandlers;
    private Task? _consumeTask;

    internal EventStreamSubscriber(
        Func<SubscriptionPosition?, IEventStreamSubscription> subscriptionFactory,
        bool isAllEventStream,
        IEventStreamConsumer consumer,
        EventMapper eventMapper)
    {
        _subscriptionFactory = subscriptionFactory;
        _isAllEventStream = isAllEventStream;
        _consumer = consumer;
        _eventMapper = eventMapper;
        _eventHandlers = CreateEventHandlers(consumer);
    }

    public void Start()
    {
        _consumeTask = Run();
    }

    public Task WaitForCompletedAsync() => _consumeTask!;

    private static Dictionary<Type, Func<EventHandlerContext, Task>> CreateEventHandlers(IEventStreamConsumer consumer)
    {
        return consumer
            .GetType()
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
            .Select(i =>
            {
                var eventType = i.GetGenericArguments()[0];
                var onEventAsyncMethod = i.GetMethod(nameof(IEventHandler<object>.OnEventAsync));
                Debug.Assert(onEventAsyncMethod != null, nameof(onEventAsyncMethod) + " != null");

                var eventContextParam = Expression.Parameter(typeof(EventHandlerContext), "context");
                var eventProperty = Expression.Property(eventContextParam, nameof(EventHandlerContext.Event));
                var positionProperty = Expression.Property(eventContextParam, nameof(EventHandlerContext.Position));
                var cancellationTokenProperty =
                    Expression.Property(eventContextParam, nameof(EventHandlerContext.CancellationToken));

                var castEventParameter = Expression.Convert(eventProperty, eventType);

                var genericEventContextType = typeof(EventHandlerContext<>).MakeGenericType(eventType);

                var genericEventContextConstructor = genericEventContextType.GetConstructor(
                    new[] { eventType, typeof(SubscriptionPosition), typeof(CancellationToken) })!;

                var genericEventContextInstance = Expression.New(genericEventContextConstructor, castEventParameter,
                    positionProperty, cancellationTokenProperty);

                var consumerInstance = Expression.Constant(consumer);
                var onEventAsyncCall =
                    Expression.Call(consumerInstance, onEventAsyncMethod, genericEventContextInstance);
                var lambda = Expression.Lambda<Func<EventHandlerContext, Task>>(onEventAsyncCall, eventContextParam);
                var compiledLambda = lambda.Compile();

                return (eventType, compiledLambda);
            })
            .ToDictionary(x => x.eventType, x => x.compiledLambda);
    }

    private async Task Run()
    {
        await _consumer.OnInitializingAsync(CancellationToken.None);

        // TODO: Get min position
        var continueAfterPosition = await _consumer.OnRestoreAsync(CancellationToken.None);

        var subscription = _subscriptionFactory(continueAfterPosition);
        await using var messageEnumerator = subscription.ConsumeAsync().GetAsyncEnumerator();

        var nextMessageTask = messageEnumerator.MoveNextAsync().AsTask();
        var tickTask = Task.Delay(TimeSpan.FromSeconds(1));

        var currentPosition = continueAfterPosition;

        while (true)
        {
            var completedTask = await Task.WhenAny(nextMessageTask, tickTask);

            if (completedTask == nextMessageTask)
            {
                if (nextMessageTask.Result)
                {
                    var message = messageEnumerator.Current;

                    switch (message)
                    {
                        case SubscriptionMessage.Event e:
                            var readEvent = e.ReadEventRecord;

                            currentPosition = _isAllEventStream
                                ? new SubscriptionPosition(readEvent.GlobalPosition.ToUInt64())
                                : new SubscriptionPosition(readEvent.StreamPosition.ToUInt64());

                            var events = _eventMapper.FromReadEvent(readEvent);
                            foreach (var @event in events)
                            {
                                if (_eventHandlers.TryGetValue(@event.GetType(), out var handler))
                                {
                                    var context = new EventHandlerContext(
                                        @event, currentPosition.Value, CancellationToken.None);

                                    await handler(context);
                                }
                            }

                            break;
                        case SubscriptionMessage.CaughtUp:
                            await _consumer.OnCaughtUpAsync(CancellationToken.None);
                            break;
                        case SubscriptionMessage.FellBehind:
                            await _consumer.OnFellBehindAsync(CancellationToken.None);
                            break;
                        case SubscriptionMessage.SubscriptionDropped dropped:
                            await _consumer.OnSubscriptionDroppedAsync(dropped.Exception, CancellationToken.None);
                            break;
                    }

                    nextMessageTask = messageEnumerator.MoveNextAsync().AsTask();
                }
            }
            else
            {
                Console.WriteLine("Tick");
                tickTask = Task.Delay(TimeSpan.FromSeconds(1));

                if (currentPosition.HasValue)
                    await _consumer.OnCheckpointAsync(currentPosition.Value, CancellationToken.None);
            }
        }
    }
}