namespace DomainBlocks.Core.Exceptions;

public class DomainBlocksException : Exception
{
    public DomainBlocksException()
    {
    }

    public DomainBlocksException(string? message) : base(message)
    {
    }

    public DomainBlocksException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}