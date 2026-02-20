namespace DomainBlocks.EventStore.MongoDB.Client.Schema2;

public static class AppendRequestFieldNames
{
    public const string StreamId = "streamId";
    public const string ExpectedStreamState = "expectedStreamState";
    public const string Events = "events";
    public const string CreatedAtUtc = "createdAtUtc";
    public const string LastSeenAtUtc = "lastSeenAtUtc";

    public static class Event
    {
        public const string EventName = "eventName";
        public const string EventData = "eventData";
        public const string Metadata = "metadata";
    }
}