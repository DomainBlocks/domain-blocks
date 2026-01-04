using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Read;

public static class ReadEvent2TransformExtensions
{
    public static IAsyncEnumerable<ReadEvent<TEventBase>> Transform<TEventBase>(
        this IAsyncEnumerable<ReadEvent<TEventBase>> source,
        IEnumerable<IReadEventTransform<TEventBase>> transforms)
        where TEventBase : class
    {
        return new TransformAsyncEnumerable<TEventBase>(source, transforms);
    }

    private class TransformAsyncEnumerable<TEventBase>(
        IAsyncEnumerable<ReadEvent<TEventBase>> source,
        IEnumerable<IReadEventTransform<TEventBase>> transforms) :
        IAsyncEnumerable<ReadEvent<TEventBase>>
        where TEventBase : class
    {
        public async IAsyncEnumerator<ReadEvent<TEventBase>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            var transformsByType = transforms.ToFrozenDictionary(x => x.SourceEventType);
            var queue = new Queue<ReadEvent<TEventBase>>();

            await foreach (var sourceEvent in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                queue.Enqueue(sourceEvent);

                while (queue.TryDequeue(out var nextEvent))
                {
                    if (transformsByType.TryGetValue(nextEvent.Event.GetType(), out var transform))
                    {
                        var transformedEvents = transform.Apply(nextEvent);

                        foreach (var transformedEvent in transformedEvents)
                            queue.Enqueue(ReadEvent.Create(transformedEvent, nextEvent.Metadata, nextEvent.Context));
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