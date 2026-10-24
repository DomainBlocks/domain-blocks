namespace DomainBlocks.EventStore.MongoDB;

// See: https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/base/error_codes.yml
internal static class MongoErrorCodes
{
    public const int ChangeStreamHistoryLost = 286;
}