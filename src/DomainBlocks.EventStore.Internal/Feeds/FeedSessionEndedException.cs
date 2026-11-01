namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// The exception that is thrown when a feed's session ends without an error. A live session should never end, so the
/// feed treats this as a lost connection.
/// </summary>
internal sealed class FeedSessionEndedException() : Exception("The feed's session ended unexpectedly.");