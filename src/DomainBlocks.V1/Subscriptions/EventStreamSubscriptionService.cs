using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions;
using DomainBlocks.V1.Abstractions.Subscriptions.Messages;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamSubscriptionService
{
    private readonly Func<SubscriptionPosition?, IEventStreamSubscription> _subscriptionFactory;
    private readonly EventStreamConsumerSession[] _sessions;
    private readonly EventMapper _eventMapper;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task _consumeTask = Task.CompletedTask;

    public EventStreamSubscriptionService(
        string name,
        Func<SubscriptionPosition?, IEventStreamSubscription> subscriptionFactory,
        IEnumerable<IEventStreamConsumer> consumers,
        EventMapper eventMapper)
    {
        Name = name;
        _subscriptionFactory = subscriptionFactory;
        _sessions = consumers.Select(x => new EventStreamConsumerSession(x)).ToArray();
        _eventMapper = eventMapper;
    }

    public string Name { get; }

    public IReadOnlyCollection<EventStreamConsumerSession> ConsumerSessions => _sessions;

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
                //Console.WriteLine("Tick");
                tickTask = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private async ValueTask HandleMessageAsync(ISubscriptionMessage message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case EventReceived eventReceived:
                await HandleEventReceivedAsync(eventReceived, cancellationToken);
                break;
            case ISubscriptionStatusMessage statusMessage:
                foreach (var session in _sessions)
                {
                    await session.NotifySubscriptionStatusAsync(statusMessage, cancellationToken);
                }

                break;
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

        foreach (var @event in mappedEvents)
        {
            foreach (var session in sessions)
            {
                await session.NotifyEventReceivedAsync(@event, message.Position, cancellationToken);
            }
        }
    }
}