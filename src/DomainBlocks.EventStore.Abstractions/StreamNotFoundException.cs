using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.Abstractions;

public sealed class StreamNotFoundException(object streamId) : DomainBlocksException($"Stream '{streamId}' not found.")
{
    public object StreamId { get; } = streamId;
}