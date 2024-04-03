using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Persistence;

namespace DomainBlocks.V1.Subscriptions;

public class CatchUpSubscriptionManager
{
    private readonly Func<GlobalPosition?, IStreamSubscription> _subscriptionFactory;
    private readonly ICatchUpSubscriptionConsumer[] _consumers;
    private readonly EventMapper _eventMapper;
    private Task? _consumeTask;

    public CatchUpSubscriptionManager(
        Func<GlobalPosition?, IStreamSubscription> subscriptionFactory,
        IEnumerable<ICatchUpSubscriptionConsumer> consumers,
        EventMapper eventMapper)
    {
        _subscriptionFactory = subscriptionFactory;
        _consumers = consumers as ICatchUpSubscriptionConsumer[] ?? consumers.ToArray();
        _eventMapper = eventMapper;
    }

    public void Start()
    {
        _consumeTask = Run();
    }

    public Task WaitForCompletedAsync() => _consumeTask!;

    private async Task Run()
    {
        await _consumers[0].OnInitializingAsync(CancellationToken.None);

        // TODO: Get min position
        var continueAfterPosition = await _consumers[0].OnLoadCheckpointAsync(CancellationToken.None);

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
                        case StreamMessage.Event e:
                            currentPosition = e.ReadEvent.GlobalPosition;

                            foreach (var consumer in _consumers)
                            {
                                var events = _eventMapper.FromReadEvent(e.ReadEvent);

                                foreach (var @event in events)
                                {
                                    await consumer.OnEventAsync(@event, CancellationToken.None);
                                }
                            }

                            break;
                        case StreamMessage.CaughtUp:
                            foreach (var consumer in _consumers)
                            {
                                await consumer.OnCaughtUpAsync(CancellationToken.None);
                            }

                            break;
                        case StreamMessage.FellBehind:
                            foreach (var consumer in _consumers)
                            {
                                await consumer.OnFellBehindAsync(CancellationToken.None);
                            }

                            break;
                        case StreamMessage.SubscriptionDropped dropped:
                            foreach (var consumer in _consumers)
                            {
                                await consumer.OnSubscriptionDroppedAsync(dropped.Exception, CancellationToken.None);
                            }

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
                    await _consumers[0].OnSaveCheckpointAsync(currentPosition.Value, CancellationToken.None);
            }
        }
    }
}