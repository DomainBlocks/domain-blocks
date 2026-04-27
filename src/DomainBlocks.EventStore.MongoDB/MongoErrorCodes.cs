namespace DomainBlocks.EventStore.MongoDB;

internal static class MongoErrorCodes
{
    public const int DuplicateKey = 11000;
    public const int WriteConflict = 112;
}