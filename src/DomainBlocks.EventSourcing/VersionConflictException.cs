using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventSourcing;

public class VersionConflictException(object stateId, string? message = null, Exception? innerException = null) :
    DomainBlocksException(message, innerException)
{
    public object StateId { get; } = stateId;
}

public sealed class VersionConflictException<TVersion>(
    object stateId,
    Optional<TVersion> expectedVersion,
    Optional<TVersion>? observedVersion = null,
    Exception? innerException = null) :
    VersionConflictException(stateId, GetMessage(stateId, expectedVersion, observedVersion), innerException)
    where TVersion : notnull
{
    public Optional<TVersion> ExpectedVersion { get; } = expectedVersion;
    public Optional<TVersion>? ObservedVersion { get; } = observedVersion;

    private static string GetMessage(
        object stateId,
        Optional<TVersion> expectedVersion,
        Optional<TVersion>? observedVersion)
    {
        return $"State with ID '{stateId}' could not be saved due to a version conflict. " +
               $"ExpectedVersion: {expectedVersion}, ObservedVersion: {observedVersion?.ToString() ?? "unavailable"}.";
    }
}