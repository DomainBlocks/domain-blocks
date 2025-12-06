using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Exception thrown when the actual state of a stream does not match the <see cref="ExpectedStreamState"/> specified
/// for an operation.
/// </summary>
public sealed class WrongExpectedStreamStateException : DomainBlocksException
{
    private WrongExpectedStreamStateException(
        string streamId,
        WrongExpectedStreamStateReason reason,
        ExpectedStreamState expectedState,
        StreamVersion actualVersion,
        Exception? inner = null) : base(BuildMessage(streamId, reason, expectedState, actualVersion), inner)
    {
        StreamId = streamId;
        Reason = reason;
        ExpectedState = expectedState;
        ActualVersion = actualVersion;
    }

    public string StreamId { get; }
    public WrongExpectedStreamStateReason Reason { get; }
    public ExpectedStreamState ExpectedState { get; }
    public StreamVersion ActualVersion { get; }

    public static WrongExpectedStreamStateException ExpectedStreamToExist(string streamId)
    {
        return new WrongExpectedStreamStateException(
            streamId,
            WrongExpectedStreamStateReason.ExpectedStreamToExist,
            ExpectedStreamState.StreamExists,
            StreamVersion.None);
    }

    public static WrongExpectedStreamStateException ExpectedStreamToNotExist(
        string streamId,
        StreamVersion actualVersion)
    {
        return new WrongExpectedStreamStateException(
            streamId,
            WrongExpectedStreamStateReason.ExpectedStreamToNotExist,
            ExpectedStreamState.StreamDoesNotExist,
            actualVersion);
    }

    public static WrongExpectedStreamStateException VersionConflict(
        string streamId,
        ExpectedStreamState expectedState,
        StreamVersion actualVersion)
    {
        return new WrongExpectedStreamStateException(
            streamId,
            WrongExpectedStreamStateReason.VersionConflict,
            expectedState,
            actualVersion);
    }

    public static WrongExpectedStreamStateException Unknown(
        string streamId,
        ExpectedStreamState expectedState,
        Exception inner)
    {
        return new WrongExpectedStreamStateException(
            streamId,
            WrongExpectedStreamStateReason.Unknown,
            expectedState,
            StreamVersion.None,
            inner);
    }

    private static string BuildMessage(
        string streamId,
        WrongExpectedStreamStateReason reason,
        ExpectedStreamState expectedState,
        StreamVersion actualVersion)
    {
        if (reason == WrongExpectedStreamStateReason.ExpectedStreamToNotExist)
            return $"Expected stream '{streamId}' to not exist, but found version {actualVersion}.";

        if (reason == WrongExpectedStreamStateReason.ExpectedStreamToExist)
            return $"Expected stream '{streamId}' to exist, but found none.";

        return $"Expected stream '{streamId}' to be version {expectedState}, but found {actualVersion}.";
    }
}