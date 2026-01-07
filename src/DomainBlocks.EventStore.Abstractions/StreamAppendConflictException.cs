using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.Abstractions;

public class StreamAppendConflictException(
    string streamId,
    ExpectedStreamState expectedState,
    StreamState? actualState = null,
    Exception? innerException = null) :
    DomainBlocksException(GetMessage(streamId, expectedState, actualState), innerException)
{
    public string StreamId { get; } = streamId;
    public ExpectedStreamState ExpectedState { get; } = expectedState;
    public StreamState? ActualState { get; } = actualState;

    private static string GetMessage(
        string streamId,
        ExpectedStreamState expectedState,
        StreamState? actualState)
    {
        return $"Append to stream '{streamId}' failed due to a conflict. " +
               $"ExpectedState: {expectedState}, ActualState: {actualState?.ToString() ?? "unavailable"}.";
    }
}