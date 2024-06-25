namespace DomainBlocks.V1.Abstractions.Exceptions;

public class WrongExpectedVersionException : Exception
{
    public WrongExpectedVersionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}