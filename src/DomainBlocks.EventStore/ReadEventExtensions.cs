using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public static class ReadEventExtensions
{
    /// <summary>
    /// Unwraps the event objects from a sequence of <see cref="ReadEvent{TEvent}"/> instances.
    /// </summary>
    public static IEnumerable<TEvent> Unwrap<TEvent>(this IEnumerable<ReadEvent<TEvent>> source)
        where TEvent : notnull
    {
        return source.Select(x => x.Event);
    }

    /// <summary>
    /// Unwraps the event objects from a sequence of <see cref="ReadEvent{TEvent}"/> instances.
    /// </summary>
    public static IAsyncEnumerable<TEvent> Unwrap<TEvent>(this IAsyncEnumerable<ReadEvent<TEvent>> source)
        where TEvent : notnull
    {
        return source.Select(x => x.Event);
    }
}