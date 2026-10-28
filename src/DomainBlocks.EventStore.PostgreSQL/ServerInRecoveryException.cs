namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when a subscription reaches a server in recovery, such as a standby or an Aurora
/// reader, instead of the primary.
/// </summary>
internal sealed class ServerInRecoveryException(string? host, Exception? innerException = null) :
    Exception($"The server at '{host}' is in recovery. Subscriptions need the primary.", innerException);