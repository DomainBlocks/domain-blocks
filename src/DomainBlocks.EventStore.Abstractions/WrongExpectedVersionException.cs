using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.Abstractions;

public class WrongExpectedVersionException(
    string streamId,
    ExpectedStreamVersion expectedVersion,
    StreamVersion actualVersion,
    Exception? innerException = null) :
    DomainBlocksException(GetMessage(streamId, expectedVersion, actualVersion), innerException)
{
    public string StreamId { get; } = streamId;
    public ExpectedStreamVersion ExpectedVersion { get; } = expectedVersion;
    public StreamVersion ActualVersion { get; } = actualVersion;

    private static string GetMessage(
        string streamId,
        ExpectedStreamVersion expectedVersion,
        StreamVersion actualVersion)
    {
        return $"Append to stream '{streamId}' failed due to a concurrency conflict. " +
               $"Expected version {expectedVersion}, but found {actualVersion}.";
    }
}