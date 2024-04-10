namespace DomainBlocks.Persistence.Exceptions;

public class WrongExpectedVersionException : Exception
{
    public WrongExpectedVersionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}