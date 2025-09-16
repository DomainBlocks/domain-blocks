namespace DomainBlocks.EventStore.Abstractions;

public static class ReadStreamResult
{
    public static ReadStreamResult<TEvent> Success<TEvent>(IAsyncEnumerable<TEvent>? events = null) =>
        new(ReadStreamStatus.Success, events ?? AsyncEnumerableEx.Empty<TEvent>());

    public static ReadStreamResult<TEvent> NotFound<TEvent>() =>
        new(ReadStreamStatus.StreamNotFound, AsyncEnumerableEx.Empty<TEvent>());
}

public sealed class ReadStreamResult<TEvent>
{
    internal ReadStreamResult(ReadStreamStatus status, IAsyncEnumerable<TEvent> events)
    {
        Status = status;
        Events = events;
    }

    public ReadStreamStatus Status { get; }
    public IAsyncEnumerable<TEvent> Events { get; }
}