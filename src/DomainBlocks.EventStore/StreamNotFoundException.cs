using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore;

public sealed class StreamNotFoundException(object streamId) : DomainBlocksException($"Stream '{streamId}' not found.")
{
    public object StreamId { get; } = streamId;
}