namespace DomainBlocks.Abstractions.Exceptions;

public class WrongExpectedVersionException : Exception
{
    public WrongExpectedVersionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}