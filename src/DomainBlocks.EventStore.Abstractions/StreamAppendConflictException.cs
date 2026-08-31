using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.Abstractions;

public class StreamAppendConflictException(string streamId, string? message = null, Exception? innerException = null) :
    DomainBlocksException(message, innerException)
{
    public string StreamId { get; } = streamId;
}

public class StreamAppendConflictException<TStreamPos>(
    string streamId,
    ExpectedStreamState<TStreamPos> expectedState,
    ObservedStreamState<TStreamPos>? observedState = null,
    Exception? innerException = null) :
    StreamAppendConflictException(streamId, GetMessage(streamId, expectedState, observedState), innerException)
    where TStreamPos : notnull
{
    public ExpectedStreamState<TStreamPos> ExpectedState { get; } = expectedState;
    public ObservedStreamState<TStreamPos>? ObservedState { get; } = observedState;

    private static string GetMessage(
        string streamId,
        ExpectedStreamState<TStreamPos> expectedState,
        ObservedStreamState<TStreamPos>? observedState)
    {
        return $"Append to stream '{streamId}' failed due to a conflict. " +
               $"ExpectedState: {expectedState}, ObservedState: {observedState?.ToString() ?? "unavailable"}.";
    }
}