using DomainBlocks.Infrastructure.MongoDB.Errors;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public static class ChangeStreamResumePolicy
{
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

        // Requires wire version 9 or higher.
        return exception is MongoException ex && ex.HasErrorLabel(ErrorLabels.ResumableChangeStreamError);
    }
}