using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public static class ReadEventExtensions
{
    public static IEnumerable<TEvent> Unwrap<TEvent>(this IEnumerable<ReadEvent<TEvent>> source)
        where TEvent : notnull
    {
        return source.Select(x => x.Event);
    }

    public static IAsyncEnumerable<TEvent> Unwrap<TEvent>(this IAsyncEnumerable<ReadEvent<TEvent>> source)
        where TEvent : notnull
    {
        return source.Select(x => x.Event);
    }
}