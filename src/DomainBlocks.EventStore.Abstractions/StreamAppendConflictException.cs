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
    StreamState<TStreamPos>? actualState = null,
    Exception? innerException = null) :
    StreamAppendConflictException(streamId, GetMessage(streamId, expectedState, actualState), innerException)
    where TStreamPos : notnull
{
    public ExpectedStreamState<TStreamPos> ExpectedState { get; } = expectedState;
    public StreamState<TStreamPos>? ActualState { get; } = actualState;

    private static string GetMessage(
        string streamId,
        ExpectedStreamState<TStreamPos> expectedState,
        StreamState<TStreamPos>? actualState)
    {
        return $"Append to stream '{streamId}' failed due to a conflict. " +
               $"ExpectedState: {expectedState}, ActualState: {actualState?.ToString() ?? "unavailable"}.";
    }
}