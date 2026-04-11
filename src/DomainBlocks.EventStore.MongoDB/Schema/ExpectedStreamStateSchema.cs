namespace DomainBlocks.EventStore.MongoDB.Schema;

internal static class ExpectedStreamStateSchema
{
    public static class FieldNames
    {
        public const string Kind = "kind";
        public const string Version = "version";
    }

    public static class Kinds
    {
        public const string Any = "any";
        public const string StreamDoesNotExist = "streamDoesNotExist";
        public const string StreamExists = "streamExists";
        public const string SpecificVersion = "specificVersion";
    }
}