using DomainBlocks.EventStore.Codecs;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class AppendRequest
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AppendRequest(
        string streamId,
        ExpectedStreamState<StreamPosition> expectedState,
        Guid commitId,
        EncodedEvent<PostgresEventData, string>[] events)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Length == 0)
            throw new ArgumentException("At least one event is required.", nameof(events));

        StreamId = streamId;
        ExpectedState = expectedState;
        CommitId = commitId;
        Events = events;
    }

    public string StreamId { get; }

    public ExpectedStreamState<StreamPosition> ExpectedState { get; }

    public Guid CommitId { get; }

    /// <summary>
    /// The events to append. The array is never empty.
    /// </summary>
    public EncodedEvent<PostgresEventData, string>[] Events { get; }

    public Task Completion => _tcs.Task;

    public bool IsCompleted => _tcs.Task.IsCompleted;

    public bool TryComplete(Exception? error = null)
    {
        return error is null ? _tcs.TrySetResult() : _tcs.TrySetException(error);
    }
}