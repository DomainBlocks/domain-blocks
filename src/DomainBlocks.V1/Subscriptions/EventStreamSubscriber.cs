using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamSubscriber
{
    private readonly Func<SubscriptionPosition?, IEventStreamSubscription> _subscriptionFactory;
    private readonly EventStreamConsumerSession[] _sessions;
    private readonly EventMapper _eventMapper;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task _consumeTask = Task.CompletedTask;

    internal EventStreamSubscriber(
        Func<SubscriptionPosition?, IEventStreamSubscription> subscriptionFactory,
        IEnumerable<IEventStreamConsumer> consumers,
        EventMapper eventMapper)
    {
        _subscriptionFactory = subscriptionFactory;
        _sessions = consumers.Select(x => new EventStreamConsumerSession(x)).ToArray();
        _eventMapper = eventMapper;
    }

    public void Start()
    {
        _consumeTask = ConsumeAllMessagesAsync(_cancellationTokenSource.Token);
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default)
    {
        return _consumeTask.WaitAsync(cancellationToken);
    }

    private async Task ConsumeAllMessagesAsync(CancellationToken cancellationToken)
    {
        var initializingTasks = _sessions.Select(x => x.InitializeAsync(cancellationToken));
        await Task.WhenAll(initializingTasks);

        var startTasks = _sessions.Select(x => x.StartAsync(cancellationToken)).ToArray();
        await Task.WhenAll(startTasks);
        var allRestoredPositions = startTasks.Select(x => x.Result).ToArray();
        var minPosition = startTasks[0].Result; // TODO
        var currentPosition = minPosition;

        var subscription = _subscriptionFactory(currentPosition);
        var messages = subscription.ConsumeAsync(cancellationToken);
        await using var messageEnumerator = messages.GetAsyncEnumerator(cancellationToken);

        var nextMessageTask = messageEnumerator.MoveNextAsync().AsTask();
        var tickTask = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var completedTask = await Task.WhenAny(nextMessageTask, tickTask);

            if (completedTask == nextMessageTask)
            {
                if (!nextMessageTask.Result) continue; // TODO

                var message = messageEnumerator.Current;
                await HandleMessageAsync(message, cancellationToken);
                nextMessageTask = messageEnumerator.MoveNextAsync().AsTask();
            }
            else
            {
                Console.WriteLine("Tick");
                tickTask = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private async Task HandleMessageAsync(SubscriptionMessage message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case SubscriptionMessage.EventReceived eventReceived:
                await HandleEventReceivedAsync(eventReceived, cancellationToken);
                break;
            default:
                foreach (var session in _sessions)
                {
                    await session.NotifyMessageAsync(message, cancellationToken);
                }

                break;
        }
    }

    private async ValueTask HandleEventReceivedAsync(
        SubscriptionMessage.EventReceived message, CancellationToken cancellationToken)
    {
        var mappedEventTypes = _eventMapper.GetMappedEventTypes(message.EventEntry).ToArray();

        // Find sessions that can handle the mapped event types.
        var sessions = _sessions.Where(x => mappedEventTypes.Any(x.CanHandleEventType)).ToArray();

        // No sessions can handle the mapped event types. Ignore.
        if (sessions.Length == 0) return;

        var mappedEvents = _eventMapper.ToEventObjects(message.EventEntry);

        foreach (var @event in mappedEvents)
        {
            foreach (var session in sessions)
            {
                await session.NotifyEventReceivedAsync(@event, message.Position, cancellationToken);
            }
        }
    }
}