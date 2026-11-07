using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore;

/// <summary>
/// The exception that is thrown when an append may have been written, but the store couldn't confirm that a failover
/// can't undo it.
/// </summary>
/// <remarks>
/// Appending again with the same commit ID is safe: if the first append was written, the retry succeeds without writing
/// it again.
/// </remarks>
public sealed class AppendNotConfirmedException(object streamId, Exception? innerException = null) :
    DomainBlocksException(
        $"Append to stream '{streamId}' may have been written, but it wasn't confirmed to survive a failover.",
        innerException)
{
    public object StreamId { get; } = streamId;
}