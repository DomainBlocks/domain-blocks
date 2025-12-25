using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Read;

public static class ReadTransformExtensions
{
    public static IAsyncEnumerable<IReadEvent<object>> Transform(
        this IAsyncEnumerable<CommittedEvent<object>> source,
        IEnumerable<IEventReadTransform> transforms)
    {
        return new TransformAsyncEnumerable(source, transforms);
    }

    private class TransformAsyncEnumerable(
        IAsyncEnumerable<CommittedEvent<object>> source,
        IEnumerable<IEventReadTransform> transforms) : IAsyncEnumerable<IReadEvent<object>>
    {
        public async IAsyncEnumerator<IReadEvent<object>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            var transformsByType = transforms.ToFrozenDictionary(x => x.SourceEventType);
            var queue = new Queue<IReadEvent<object>>();

            await foreach (var sourceEvent in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                queue.Enqueue(sourceEvent);

                while (queue.TryDequeue(out var nextEvent))
                {
                    if (transformsByType.TryGetValue(nextEvent.Payload.GetType(), out var transform))
                    {
                        var transformedEvents = transform.Apply(nextEvent.Payload, nextEvent.Header);

                        foreach (var transformedEvent in transformedEvents)
                            queue.Enqueue(new TransformedEvent<object>(nextEvent.Header, transformedEvent));
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