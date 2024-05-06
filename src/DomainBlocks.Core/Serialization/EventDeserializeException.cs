namespace DomainBlocks.Core.Serialization;

public class EventDeserializeException : Exception
{
    public EventDeserializeException(string message) : base(message)
    {
    }

    public EventDeserializeException(string message, Exception inner) : base(message, inner)
    {
    }
}