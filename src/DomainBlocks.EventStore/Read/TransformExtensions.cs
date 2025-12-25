using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Read;

public static class TransformExtensions
{
    public static IAsyncEnumerable<IReadEvent<TEventBase>> Transform<TEventBase>(
        this IAsyncEnumerable<CommittedEvent<TEventBase>> source,
        IEnumerable<IEventReadTransform<TEventBase>> transforms)
        where TEventBase : class
    {
        return new TransformAsyncEnumerable<TEventBase>(source, transforms);
    }

    private class TransformAsyncEnumerable<TEventBase>(
        IAsyncEnumerable<CommittedEvent<TEventBase>> source,
        IEnumerable<IEventReadTransform<TEventBase>> transforms) :
        IAsyncEnumerable<IReadEvent<TEventBase>>
        where TEventBase : class
    {
        public async IAsyncEnumerator<IReadEvent<TEventBase>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            var transformsByType = transforms.ToFrozenDictionary(x => x.SourceEventType);
            var queue = new Queue<IReadEvent<TEventBase>>();

            await foreach (var sourceEvent in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                queue.Enqueue(sourceEvent);

                while (queue.TryDequeue(out var nextEvent))
                {
                    if (transformsByType.TryGetValue(nextEvent.Payload.GetType(), out var transform))
                    {
                        var transformedEvents = transform.Apply(nextEvent.Payload, nextEvent.Header);

                        foreach (var transformedEvent in transformedEvents)
                            queue.Enqueue(new TransformedEvent<TEventBase>(nextEvent.Header, transformedEvent));
                    }
                    else
                    {
                        yield return nextEvent;
                    }
                }
            }
        }
    }
}