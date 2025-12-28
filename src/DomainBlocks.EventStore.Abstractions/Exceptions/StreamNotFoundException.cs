using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.Abstractions.Exceptions;

public sealed class StreamNotFoundException(string streamId) : DomainBlocksException($"Stream '{streamId}' not found.")
{
    public string StreamId { get; } = streamId;
}