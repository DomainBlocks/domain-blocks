using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Transforms;

public static class ReadEventTransformExtensions
{
    public static IAsyncEnumerable<ReadEvent<TEventBase, TStreamId, TStreamPos, TLogPos>>
        Transform<TEventBase, TStreamId, TStreamPos, TLogPos>(
            this IAsyncEnumerable<ReadEvent<TEventBase, TStreamId, TStreamPos, TLogPos>> source,
            IEnumerable<IReadEventTransform<TEventBase, TStreamId, TStreamPos, TLogPos>> transforms)
        where TEventBase : class
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new TransformAsyncEnumerable<TEventBase, TStreamId, TStreamPos, TLogPos>(source, transforms);
    }

    private class TransformAsyncEnumerable<TEventBase, TStreamId, TStreamPos, TLogPos>(
        IAsyncEnumerable<ReadEvent<TEventBase, TStreamId, TStreamPos, TLogPos>> source,
        IEnumerable<IReadEventTransform<TEventBase, TStreamId, TStreamPos, TLogPos>> transforms) :
        IAsyncEnumerable<ReadEvent<TEventBase, TStreamId, TStreamPos, TLogPos>>
        where TEventBase : class
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        public async IAsyncEnumerator<ReadEvent<TEventBase, TStreamId, TStreamPos, TLogPos>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            var transformsByType = transforms.ToFrozenDictionary(x => x.SourceEventType);
            var queue = new Queue<ReadEvent<TEventBase, TStreamId, TStreamPos, TLogPos>>();

            await foreach (var sourceEvent in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                queue.Enqueue(sourceEvent);

                while (queue.TryDequeue(out var nextEvent))
                {
                    if (transformsByType.TryGetValue(nextEvent.Event.GetType(), out var transform))
                    {
                        var transformedEvents = transform.Apply(nextEvent);

                        foreach (var transformedEvent in transformedEvents)
                            queue.Enqueue(ReadEvent.Create(transformedEvent, nextEvent.Context));
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