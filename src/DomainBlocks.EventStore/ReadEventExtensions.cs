using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public static class ReadEventExtensions
{
    extension<TEvent, TStreamId, TStreamPos, TLogPos>(
        IEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> source)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        /// <summary>
        /// Unwraps the event objects from a sequence of
        /// <see cref="ReadEvent{TEvent,TStreamId,TStreamPos,TLogPos}"/> instances.
        /// </summary>
        public IEnumerable<TEvent> Unwrap() => source.Select(x => x.Event);
    }

    extension<TEvent, TStreamId, TStreamPos, TLogPos>(
        IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> source)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        /// <summary>
        /// Unwraps the event objects from a sequence of
        /// <see cref="ReadEvent{TEvent,TStreamId,TStreamPos,TLogPos}"/> instances.
        /// </summary>
        public IAsyncEnumerable<TEvent> Unwrap() => source.Select(x => x.Event);
    }
}