namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// The feed reached a server in recovery, such as a standby or an Aurora reader, rather than the primary, so it tries
/// again later.
/// </summary>
internal sealed class ReplicationServerInRecoveryException(string? host) :
    Exception($"The server at '{host}' is in recovery, so the feed can't use it.");