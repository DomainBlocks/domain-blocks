namespace DomainBlocks.EventStore.Abstractions;

public enum WrongExpectedStreamStateReason
{
    ExpectedStreamToExist,
    ExpectedStreamToNotExist,
    VersionConflict
}