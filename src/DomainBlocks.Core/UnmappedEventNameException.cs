namespace DomainBlocks.Core;

public class UnmappedEventNameException : Exception
{
    public UnmappedEventNameException(string eventName) :
        this(eventName, $"Event name '{eventName}' could not be mapped to a CLR type")
    {
    }

    public UnmappedEventNameException(string eventName, string message) : base(message)
    {
        EventName = eventName;
    }

    public string? EventName { get; }
}