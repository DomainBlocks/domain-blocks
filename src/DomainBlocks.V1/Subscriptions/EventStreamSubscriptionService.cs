using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using DomainBlocks.V1.Abstractions.Subscriptions;
using DomainBlocks.V1.Abstractions.Subscriptions.Messages;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamSubscriptionService : IEventStreamSubscriptionStatusSource
{
    private readonly Func<SubscriptionPosition?, IEventStreamSubscription> _subscriptionFactory;
    private readonly EventStreamConsumerSession[] _sessions;
    private readonly EventMapper _eventMapper;
    private readonly IReadOnlyDictionary<Type, EventWrapperFactory> _eventWrapperFactories;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task _consumeTask = Task.CompletedTask;
    private volatile EventStreamSubscriptionStatus _subscriptionStatus = EventStreamSubscriptionStatus.Unsubscribed;

    public EventStreamSubscriptionService(
        string name,
        Func<SubscriptionPosition?, IEventStreamSubscription> subscriptionFactory,
        EventMapper eventMapper,
        IEnumerable<IEventStreamConsumer> consumers)
    {
        Name = name;
        _subscriptionFactory = subscriptionFactory;
        _sessions = consumers.Select(x => new EventStreamConsumerSession(x, this)).ToArray();
        _eventMapper = eventMapper;
        _eventWrapperFactories = eventMapper.EventTypes.ToDictionary(x => x, CreateEventWrapperFactory);
    }

    public string Name { get; }

    public IReadOnlyCollection<EventStreamConsumerSession> ConsumerSessions => _sessions;

    public EventStreamSubscriptionStatus SubscriptionStatus => _subscriptionStatus;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var initializingTasks = _sessions.Select(x => x.InitializeAsync(cancellationToken));
        await Task.WhenAll(initializingTasks);
        _consumeTask = ConsumeAllMessagesAsync(_cancellationTokenSource.Token);
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default)
    {
        return _consumeTask.WaitAsync(cancellationToken);
    }

    private static EventWrapperFactory CreateEventWrapperFactory(Type eventType)
    {
        var eventWrapperType = typeof(EventWrapper<>).MakeGenericType(eventType);

        var eventWrapperConstructor = eventWrapperType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public, new[] { eventType, typeof(SubscriptionPosition) });

        Debug.Assert(eventWrapperConstructor != null, nameof(eventWrapperConstructor) + " != null");

        var eventParam = Expression.Parameter(typeof(object), "event");
        var eventCastParam = Expression.Convert(eventParam, eventType);
        var positionParam = Expression.Parameter(typeof(SubscriptionPosition), "position");
        var newExpression = Expression.New(eventWrapperConstructor, eventCastParam, positionParam);
        var lambdaExpression = Expression.Lambda<EventWrapperFactory>(newExpression, eventParam, positionParam);

        return lambdaExpression.Compile();
    }

    private async Task ConsumeAllMessagesAsync(CancellationToken cancellationToken)
    {
        var startTasks = _sessions.Select(x => x.StartAsync(cancellationToken)).ToArray();
        await Task.WhenAll(startTasks);
        var allStartPositions = startTasks.Select(x => x.Result).ToArray();

        var minPosition = allStartPositions.All(x => x == null)
            ? new SubscriptionPosition?()
            : allStartPositions.Select(x => x!.Value).MinBy(x => x.Value);

        var currentPosition = minPosition;

        var subscription = _subscriptionFactory(currentPosition);

        _subscriptionStatus = EventStreamSubscriptionStatus.CatchingUp;

        await foreach (var message in subscription.ConsumeAsync(cancellationToken))
        {
            await HandleMessageAsync(message);
        }

        return;

        async ValueTask HandleMessageAsync(ISubscriptionMessage message)
        {
            switch (message)
            {
                case EventReceived eventReceived:
                    await HandleEventReceivedAsync(eventReceived, cancellationToken);
                    currentPosition = eventReceived.Position;
                    break;
                case ISubscriptionStatusMessage statusMessage:
                    await HandleSubscriptionStatusMessageAsync(statusMessage, cancellationToken);
                    break;
            }
        }
    }

    private async ValueTask HandleEventReceivedAsync(EventReceived message, CancellationToken cancellationToken)
    {
        var mappedEventTypes = _eventMapper.GetMappedEventTypes(message.EventRecord).ToArray();

        // Find sessions that can handle the mapped event types.
        var sessions = _sessions.Where(x => mappedEventTypes.Any(x.CanHandleEventType)).ToArray();

        // No sessions can handle the mapped event types. Ignore.
        if (sessions.Length == 0) return;

        var mappedEvents = _eventMapper.ToEventObjects(message.EventRecord);
        var wrappedEvents = mappedEvents.Select(x => _eventWrapperFactories[x.GetType()](x, message.Position));

        foreach (var @event in mappedEvents)
        {
            foreach (var session in sessions)
            {
                await session.NotifyEventReceivedAsync(@event, message.Position, cancellationToken);
            }
        }
    }

    private async ValueTask HandleSubscriptionStatusMessageAsync(
        ISubscriptionStatusMessage statusMessage, CancellationToken cancellationToken)
    {
        _subscriptionStatus = statusMessage switch
        {
            CaughtUp => EventStreamSubscriptionStatus.Live,
            FellBehind => EventStreamSubscriptionStatus.CatchingUp,
            SubscriptionDropped => EventStreamSubscriptionStatus.Unsubscribed,
            _ => _subscriptionStatus
        };

        foreach (var session in _sessions)
        {
            await session.NotifySubscriptionStatusAsync(statusMessage, cancellationToken);
        }
    }
}