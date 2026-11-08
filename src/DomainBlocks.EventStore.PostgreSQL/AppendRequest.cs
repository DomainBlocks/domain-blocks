using DomainBlocks.EventStore.Codecs;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class AppendRequest
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AppendRequest? _nextAttached;

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

    public bool TryComplete(Exception? error = null)
    {
        if (!TrySetOutcome(error))
            return false;

        for (var request = _nextAttached; request is not null; request = request._nextAttached)
            request.TrySetOutcome(error);

        return true;
    }

    /// <summary>
    /// Makes another request with this request's commit ID complete with this request's outcome. Called while requests
    /// are taken from the queue, before either request can have been completed.
    /// </summary>
    public void Attach(AppendRequest request)
    {
        request._nextAttached = _nextAttached;
        _nextAttached = request;
    }

    private bool TrySetOutcome(Exception? error)
    {
        return error is null ? _tcs.TrySetResult() : _tcs.TrySetException(error);
    }
}