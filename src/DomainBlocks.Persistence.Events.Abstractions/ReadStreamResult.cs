namespace DomainBlocks.Persistence.Events.Abstractions;

public sealed class ReadStreamResult<TPayload>
{
    private ReadStreamResult(ReadStreamStatus status, IAsyncEnumerable<EventRecord<TPayload>> events)
    {
        Status = status;
        Events = events;
    }

    public ReadStreamStatus Status { get; }
    public IAsyncEnumerable<EventRecord<TPayload>> Events { get; }

    public static ReadStreamResult<TPayload> NotFound() =>
        new(ReadStreamStatus.StreamNotFound, AsyncEnumerableEx.Empty<EventRecord<TPayload>>());

    public static ReadStreamResult<TPayload> Success(IAsyncEnumerable<EventRecord<TPayload>> events) =>
        new(ReadStreamStatus.Success, events);
}