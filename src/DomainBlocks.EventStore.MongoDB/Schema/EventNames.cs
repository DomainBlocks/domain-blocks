namespace DomainBlocks.EventStore.MongoDB.Schema;

public static class EventNames
{
    public const string DuplicatesSkipped = "DuplicatesSkipped";
    public const string ConflictsRejected = "ConflictsRejected";
}