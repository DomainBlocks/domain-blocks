namespace DomainBlocks.Core.Subscriptions;

public sealed class CompositeEventStreamSubscriber<TEvent, TPosition> :
    IEventStreamSubscriber<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly IReadOnlyCollection<IEventStreamSubscriber<TEvent, TPosition>> _subscribers;

    public CompositeEventStreamSubscriber(IEnumerable<IEventStreamSubscriber<TEvent, TPosition>> subscribers)
    {
        _subscribers = subscribers as IReadOnlyCollection<IEventStreamSubscriber<TEvent, TPosition>> ??
                       subscribers.ToList();

        if (_subscribers.Count == 0)
        {
            throw new ArgumentException("Expected at least one subscriber", nameof(subscribers));
        }
    }

    public CheckpointFrequency CatchUpCheckpointFrequency => _subscribers.First().CatchUpCheckpointFrequency;
    public CheckpointFrequency LiveCheckpointFrequency => _subscribers.First().LiveCheckpointFrequency;

    public async Task<TPosition?> OnStarting(CancellationToken cancellationToken)
    {
        var tasks = _subscribers.Select(x => x.OnStarting(cancellationToken)).ToArray();
        var positions = await Task.WhenAll(tasks);

        // TODO (DS): Add ability to specify checkpoint frequency at the top level of the builder chain - which can
        // apply to all subscribers.
        return positions.First();
    }

    public Task OnCatchingUp(CancellationToken cancellationToken)
    {
        var tasks = _subscribers.Select(x => x.OnCatchingUp(cancellationToken));
        return Task.WhenAll(tasks);
    }

    public async Task<OnEventResult> OnEvent(TEvent @event, TPosition position, CancellationToken cancellationToken)
    {
        var tasks = _subscribers.Select(x => OnEvent(@event, position, x, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.FirstOrDefault(x => x == OnEventResult.Processed, OnEventResult.Ignored);
    }

    public Task OnCheckpoint(TPosition position, CancellationToken cancellationToken)
    {
        var tasks = _subscribers.Select(x => x.OnCheckpoint(position, cancellationToken));
        return Task.WhenAll(tasks);
    }

    public Task OnLive(CancellationToken cancellationToken)
    {
        var tasks = _subscribers.Select(x => x.OnLive(cancellationToken));
        return Task.WhenAll(tasks);
    }

    public Task<EventErrorResolution> OnEventError(
        TEvent @event,
        TPosition position,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // If we get here, then the composite event handling in this class resulted in an error.
        // The only option is to abort.
        return Task.FromResult(EventErrorResolution.Abort);
    }

    public Task OnSubscriptionDropped(
        SubscriptionDroppedReason reason,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        var tasks = _subscribers.Select(x => x.OnSubscriptionDropped(reason, exception, cancellationToken));
        return Task.WhenAll(tasks);
    }

    private static async Task<OnEventResult> OnEvent(
        TEvent @event,
        TPosition position,
        IEventStreamSubscriber<TEvent, TPosition> subscriber,
        CancellationToken cancellationToken)
    {
        var isHandled = false;
        var result = OnEventResult.Ignored;

        do
        {
            try
            {
                result = await subscriber.OnEvent(@event, position, cancellationToken);
                isHandled = true;
                break;
            }
            catch (Exception ex)
            {
                var resolution = await subscriber.OnEventError(@event, position, ex, cancellationToken);

                switch (resolution)
                {
                    case EventErrorResolution.Abort:
                        throw;
                    case EventErrorResolution.Retry:
                        break;
                    case EventErrorResolution.Skip:
                        isHandled = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected EventErrorResolution value {resolution}");
                }
            }
        } while (!isHandled);

        return result;
    }
}