using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamSubscriber
{
    private readonly Func<GlobalPosition?, IStreamSubscription> _subscriptionFactory;
    private readonly ICatchUpSubscriptionConsumer[] _consumers;
    private Task? _consumeTask;

    public EventStreamSubscriber(
        Func<GlobalPosition?, IStreamSubscription> subscriptionFactory,
        IEnumerable<ICatchUpSubscriptionConsumer> consumers)
    {
        _subscriptionFactory = subscriptionFactory;
        _consumers = consumers as ICatchUpSubscriptionConsumer[] ?? consumers.ToArray();
    }

    public void Start()
    {
        _consumeTask = Run();
    }

    public Task WaitForCompletedAsync() => _consumeTask!;

    private async Task Run()
    {
        await _consumers[0].OnInitializing(CancellationToken.None);

        // TODO: Get min position
        var continueAfterPosition = await _consumers[0].OnLoadCheckpointAsync(CancellationToken.None);

        var subscription = _subscriptionFactory(continueAfterPosition);
        await using var messageEnumerator = subscription.ConsumeAsync().GetAsyncEnumerator();

        var nextMessageTask = messageEnumerator.MoveNextAsync().AsTask();
        var tickTask = Task.Delay(TimeSpan.FromSeconds(1));

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
                            foreach (var consumer in _consumers)
                            {
                                await consumer.OnEvent(e.ReadEvent, CancellationToken.None);
                            }

                            break;
                        case StreamMessage.CaughtUp:
                            foreach (var consumer in _consumers)
                            {
                                await consumer.OnCaughtUp(CancellationToken.None);
                            }

                            break;
                        case StreamMessage.FellBehind:
                            foreach (var consumer in _consumers)
                            {
                                await consumer.OnFellBehind(CancellationToken.None);
                            }

                            break;
                        case StreamMessage.SubscriptionDropped dropped:
                            foreach (var consumer in _consumers)
                            {
                                await consumer.OnSubscriptionDropped(dropped.Exception, CancellationToken.None);
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
            }
        }
    }
}