namespace DomainBlocks.Persistence.Abstractions.Events;

public sealed class ReadStreamResult<TEvent>
{
    private ReadStreamResult(ReadStreamStatus status, IAsyncEnumerable<TEvent> events)
    {
        Status = status;
        Events = events;
    }

    public ReadStreamStatus Status { get; }
    public IAsyncEnumerable<TEvent> Events { get; }

    public static ReadStreamResult<TEvent> NotFound() =>
        new(ReadStreamStatus.StreamNotFound, AsyncEnumerableEx.Empty<TEvent>());

    public static ReadStreamResult<TEvent> Success(IAsyncEnumerable<TEvent> events) =>
        new(ReadStreamStatus.Success, events);
}