namespace DomainBlocks.Persistence.Abstractions.Events;

public sealed class ReadStreamResult<TPayload>
{
    private ReadStreamResult(ReadStreamStatus status, IAsyncEnumerable<StoredEventData<TPayload>> events)
    {
        Status = status;
        Events = events;
    }

    public ReadStreamStatus Status { get; }
    public IAsyncEnumerable<StoredEventData<TPayload>> Events { get; }

    public static ReadStreamResult<TPayload> NotFound() =>
        new(ReadStreamStatus.StreamNotFound, AsyncEnumerableEx.Empty<StoredEventData<TPayload>>());

    public static ReadStreamResult<TPayload> Success(IAsyncEnumerable<StoredEventData<TPayload>> events) =>
        new(ReadStreamStatus.Success, events);
}