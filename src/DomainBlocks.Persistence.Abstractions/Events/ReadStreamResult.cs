namespace DomainBlocks.Persistence.Abstractions.Events;

public sealed class ReadStreamResult<TEvent>(ReadStreamStatus status, IAsyncEnumerable<TEvent> events)
{
    public ReadStreamStatus Status { get; } = status;
    public IAsyncEnumerable<TEvent> Events { get; } = events;

    public static ReadStreamResult<TEvent> NotFound() =>
        new(ReadStreamStatus.StreamNotFound, AsyncEnumerableEx.Empty<TEvent>());

    public static ReadStreamResult<TEvent> Success(IAsyncEnumerable<TEvent> events) =>
        new(ReadStreamStatus.Success, events);
}