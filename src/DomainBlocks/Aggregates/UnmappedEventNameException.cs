using System;
using System.Runtime.Serialization;

namespace DomainBlocks.Aggregates;

[Serializable]
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

    public UnmappedEventNameException(string eventName, string message, Exception inner) :
        base(message, inner)
    {
        EventName = eventName;
    }

    protected UnmappedEventNameException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        EventName = (string)info.GetValue(nameof(EventName), typeof(string));
    }

    public string EventName { get; }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(EventName), EventName);
    }
}