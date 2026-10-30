using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal static class ChangeStreamTransientErrors
{
    // Lost history can't be resumed from, but it can be recovered from: the next session finds the resume token gone
    // and starts a new change stream.
    public static bool IsTransient(Exception exception) =>
        CanResume(exception) || exception is MongoCommandException ex && IsHistoryLost(ex);

    public static bool IsHistoryLost(MongoCommandException exception) =>
        exception.Code == MongoErrorCodes.ChangeStreamHistoryLost;

    private static bool CanResume(Exception exception)
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
}