using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when a read or a subscription reaches a server in recovery, such as a standby or an
/// Aurora reader, instead of the primary.
/// </summary>
public sealed class ServerInRecoveryException(string? host, Exception? innerException = null) :
    DomainBlocksException($"The server at '{host}' is in recovery. The store needs the primary.", innerException)
{
    public string? Host { get; } = host;
}