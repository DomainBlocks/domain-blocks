using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Transforms;

public static class ReadEventTransformExtensions
{
    public static IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>
        Transform<TEvent, TStreamId, TStreamPos, TLogPos>(
            this IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> source,
            IEnumerable<IReadEventTransform<TEvent, TStreamId, TStreamPos, TLogPos>> transforms)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new TransformAsyncEnumerable<TEvent, TStreamId, TStreamPos, TLogPos>(source, transforms);
    }

    private class TransformAsyncEnumerable<TEvent, TStreamId, TStreamPos, TLogPos>(
        IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> source,
        IEnumerable<IReadEventTransform<TEvent, TStreamId, TStreamPos, TLogPos>> transforms) :
        IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        public async IAsyncEnumerator<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            var transformsByType = transforms.ToFrozenDictionary(x => x.SourceEventType);
            var queue = new Queue<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>();

            await foreach (var sourceEvent in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                queue.Enqueue(sourceEvent);

                while (queue.TryDequeue(out var nextEvent))
                {
                    if (transformsByType.TryGetValue(nextEvent.Payload.GetType(), out var transform))
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