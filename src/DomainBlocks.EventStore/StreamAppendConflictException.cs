using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore;

public class StreamAppendConflictException(object streamId, string? message = null, Exception? innerException = null) :
    DomainBlocksException(message, innerException)
{
    public object StreamId { get; } = streamId;
}

public class StreamAppendConflictException<TStreamPos>(
    object streamId,
    ExpectedStreamState<TStreamPos> expectedState,
    ObservedStreamState<TStreamPos> observedState = default,
    Exception? innerException = null) :
    StreamAppendConflictException(streamId, GetMessage(streamId, expectedState, observedState), innerException)
    where TStreamPos : notnull
{
    public ExpectedStreamState<TStreamPos> ExpectedState { get; } = expectedState;

    /// <summary>
    /// The stream state found in place of the expected one. The default value, which matches <see langword="null"/>,
    /// means the store did not observe it.
    /// </summary>
    public ObservedStreamState<TStreamPos> ObservedState { get; } = observedState;

    private static string GetMessage(
        object streamId,
        ExpectedStreamState<TStreamPos> expectedState,
        ObservedStreamState<TStreamPos> observedState)
    {
        return $"Append to stream '{streamId}' failed due to a conflict. " +
               $"ExpectedState: {expectedState}, ObservedState: {observedState}.";
    }
}