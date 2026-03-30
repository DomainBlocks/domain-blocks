namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

public static class StreamStateSchema
{
    public static class FieldNames
    {
        public const string Kind = "kind";
        public const string Version = "version";
    }

    public static class Kinds
    {
        public const string StreamDoesNotExist = "streamDoesNotExist";
        public const string StreamExists = "streamExists";
    }
}