using System;
using System.Runtime.Serialization;

namespace DomainBlocks.Aggregates;

[Serializable]
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

    public UnmappedEventTypeException(string eventTypeFullName, string message, Exception inner) :
        base(message, inner)
    {
        EventTypeFullName = eventTypeFullName;
    }

    protected UnmappedEventTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        EventTypeFullName = (string)info.GetValue(nameof(EventTypeFullName), typeof(string));
    }

    public string EventTypeFullName { get; }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(EventTypeFullName), EventTypeFullName);
    }
}