namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Represents one append and all its retries.
/// </summary>
/// <param name="number">The operation's number, which its events carry.</param>
/// <param name="streamId">The stream that the operation appended to.</param>
/// <param name="eventCount">The number of events that the operation appended.</param>
/// <param name="outcome">How the operation ended.</param>
/// <param name="errors">The type name of the error that ended each failed attempt, in order.</param>
internal sealed class AppendOperation(
    long number,
    string streamId,
    int eventCount,
    AppendOutcome outcome,
    IReadOnlyList<string> errors)
{
    public long Number { get; } = number;

    public string StreamId { get; } = streamId;

    public int EventCount { get; } = eventCount;

    public AppendOutcome Outcome { get; } = outcome;

    public IReadOnlyList<string> Errors { get; } = errors;
}