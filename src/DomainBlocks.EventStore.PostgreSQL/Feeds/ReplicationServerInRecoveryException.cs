namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// The exception that is thrown when a feed reaches a server in recovery, such as a standby or an Aurora reader,
/// instead of the primary.
/// </summary>
internal sealed class ReplicationServerInRecoveryException(string? host) :
    Exception($"The server at '{host}' is in recovery. Live subscriptions need the primary.");