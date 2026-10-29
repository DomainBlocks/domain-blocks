using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

/// <summary>
/// Represents where a feed's next change stream resumes: after the last change that the feed delivered or, before the
/// first, after the point that the server reported when the stream opened.
/// </summary>
/// <remarks>
/// Each session records tokens on it as it delivers changes, and the session factory resumes the next change stream
/// after the last one recorded.
/// </remarks>
internal sealed class ChangeStreamResumePoint
{
    /// <summary>
    /// Gets the token that the next change stream resumes after, or <see langword="null"/> if there is none yet.
    /// </summary>
    public BsonDocument? ResumeToken { get; private set; }

    /// <summary>
    /// Records a token that the next change stream can resume after.
    /// </summary>
    public void Record(BsonDocument resumeToken) => ResumeToken = resumeToken;
}