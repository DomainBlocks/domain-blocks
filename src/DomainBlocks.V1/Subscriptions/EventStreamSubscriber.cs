using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamSubscriber
{
    private readonly Func<SubscriptionPosition?, IEventStreamSubscription> _subscriptionFactory;
    private readonly EventStreamConsumerSession[] _sessions;
    private readonly EventMapper _eventMapper;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _consumeTask;

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
        _cancellationTokenSource = new CancellationTokenSource();
        _consumeTask = ConsumeAllMessagesAsync(_cancellationTokenSource.Token);
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default)
    {
        if (_consumeTask == null)
        {
            throw new InvalidOperationException("Subscription is not started.");
        }

        return _consumeTask.WaitAsync(cancellationToken);
    }

    private async Task ConsumeAllMessagesAsync(CancellationToken cancellationToken)
    {
        var initializingTasks = _sessions.Select(x => x.InitializeAsync(cancellationToken));
        await Task.WhenAll(initializingTasks);

        var restoreTasks = _sessions.Select(x => x.RestoreAsync(cancellationToken)).ToArray();
        await Task.WhenAll(restoreTasks);
        var allRestoredPositions = restoreTasks.Select(x => x.Result).ToArray();
        var minPosition = restoreTasks[0].Result; // TODO
        var currentPosition = minPosition;

        foreach (var session in _sessions)
        {
            session.Start();
        }

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
                await HandleEventReceivedMessageAsync(eventReceived, cancellationToken);
                break;
            case SubscriptionMessage.CaughtUp caughtUp:
                await HandleCaughtUpMessageAsync(caughtUp, cancellationToken);
                break;
            case SubscriptionMessage.FellBehind fellBehind:
                await HandleFellBehindMessageAsync(fellBehind, cancellationToken);
                break;
            case SubscriptionMessage.SubscriptionDropped subscriptionDropped:
                await HandleSubscriptionDroppedMessageAsync(subscriptionDropped, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(message));
        }
    }

    private async ValueTask HandleEventReceivedMessageAsync(
        SubscriptionMessage.EventReceived message, CancellationToken cancellationToken)
    {
        var mappedEventTypes = _eventMapper.GetMappedEventTypes(message.EventEntry).ToArray();

        // Find sessions that can handle the mapped event types.
        var sessions = _sessions.Where(x => mappedEventTypes.Any(x.CanHandle)).ToArray();

        // No sessions can handle the mapped event types. Ignore.
        if (sessions.Length == 0) return;

        var mappedEvents = _eventMapper.ToEventObjects(message.EventEntry);

        var eventHandlerContexts = mappedEvents
            .Select(x => new EventHandlerContext(x, message.Position, cancellationToken));

        foreach (var context in eventHandlerContexts)
        {
            foreach (var session in sessions)
            {
                await session.NotifyEventReceivedAsync(context, cancellationToken);
            }
        }
    }

    private async Task HandleCaughtUpMessageAsync(
        SubscriptionMessage.CaughtUp message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    private async Task HandleFellBehindMessageAsync(
        SubscriptionMessage.FellBehind message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    private async Task HandleSubscriptionDroppedMessageAsync(
        SubscriptionMessage.SubscriptionDropped message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }
}