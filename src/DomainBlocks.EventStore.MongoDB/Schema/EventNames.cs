namespace DomainBlocks.EventStore.MongoDB.Schema;

internal static class EventNames
{
    public const string DuplicatesSkipped = "DuplicatesSkipped";
    public const string ConflictsRejected = "ConflictsRejected";
}