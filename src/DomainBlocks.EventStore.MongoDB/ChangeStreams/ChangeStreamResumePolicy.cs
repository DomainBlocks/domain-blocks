using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal static class ChangeStreamResumePolicy
{
    // See: https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/base/error_codes.yml
    private const int ChangeStreamHistoryLostCode = 286;

    public static bool CanResume(Exception exception)
    {
        // See: https://github.com/mongodb/specifications/blob/master/source/change-streams/change-streams.md#resumable-error
        if (exception is
            MongoConnectionException { IsNetworkException: true } or
            MongoConnectionPoolPausedException or
            MongoCursorNotFoundException or
            TimeoutException)
        {
            return true;
        }

        // The resumable error label requires wire version 9 or higher.
        return exception is MongoException ex && ex.HasErrorLabel(MongoErrorLabels.ResumableChangeStreamError);
    }

    /// <summary>
    /// Whether the oplog no longer holds the point that the change stream would resume from. The stream cannot resume,
    /// but a new one can start from the current point.
    /// </summary>
    public static bool IsHistoryLost(Exception exception) =>
        exception is MongoCommandException { Code: ChangeStreamHistoryLostCode };
}