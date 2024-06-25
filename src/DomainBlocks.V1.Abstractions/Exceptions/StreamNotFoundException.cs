namespace DomainBlocks.V1.Abstractions.Exceptions;

public class StreamNotFoundException : Exception
{
    public StreamNotFoundException(string message) : base(message)
    {
    }

    public StreamNotFoundException(string message, Exception inner) : base(message, inner)
    {
    }
}