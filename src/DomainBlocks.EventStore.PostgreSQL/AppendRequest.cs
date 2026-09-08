using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A single append operation awaiting commit. Completed by the appender once the database has reported its outcome.
/// </summary>
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
    /// The encoded events to append. Always contains at least one event.
    /// </summary>
    public EncodedEvent<PostgresEventData, string>[] Events { get; }

    public Task Completion => _tcs.Task;

    public bool IsCompleted => _tcs.Task.IsCompleted;

    /// <summary>
    /// Attempts to complete this request successfully, or with a failure if an exception is provided.
    /// </summary>
    /// <returns>True if the request was completed by this call; false if it had already been completed.</returns>
    public bool TryComplete(Exception? error = null)
    {
        return error is null ? _tcs.TrySetResult() : _tcs.TrySetException(error);
    }
}
