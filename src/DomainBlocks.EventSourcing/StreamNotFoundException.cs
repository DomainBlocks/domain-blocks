using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventSourcing;

public class StreamNotFoundException : DomainBlocksException
{
    public StreamNotFoundException(string message) : base(message)
    {
    }

    public StreamNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}