namespace DomainBlocks.Persistence.Abstractions.Events.Exceptions;

public class StreamConcurrencyException(
    string streamId,
    long expectedVersion,
    long actualVersion,
    Exception? innerException = null) :
    Exception(GetMessage(streamId, expectedVersion, actualVersion), innerException)
{
    public string StreamId { get; } = streamId;
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;

    private static string GetMessage(string streamId, long expectedVersion, long actualVersion)
    {
        return $"Append to stream '{streamId}' failed due to a concurrency conflict. " +
               $"Expected version {expectedVersion}, but found {actualVersion}.";
    }
}