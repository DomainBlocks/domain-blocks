using System.Runtime.Serialization;

namespace DomainBlocks.Core;

public class UnmappedEventTypeException : Exception
{
    public UnmappedEventTypeException(string eventTypeFullName) :
        this(eventTypeFullName, $"CLR type '{eventTypeFullName}' could not be mapped to an event name")
    {
    }

    public UnmappedEventTypeException(string eventTypeFullName, string message) : base(message)
    {
        EventTypeFullName = eventTypeFullName;
    }

    public string? EventTypeFullName { get; }
}