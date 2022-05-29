using System;
using System.Runtime.Serialization;

namespace DomainBlocks.Serialization;

[Serializable]
public class InvalidEventTypeException : EventDeserializeException
{
    public InvalidEventTypeException(string serializedEventType, string clrTypeName) :
        this(serializedEventType, clrTypeName, $"Cannot cast event of type {serializedEventType} to {clrTypeName}")
    {
        SerializedEventType = serializedEventType;
        ClrTypeName = clrTypeName;
    }

    public InvalidEventTypeException(string serializedEventType, string clrTypeName, string message) 
        : base(message)
    {
        SerializedEventType = serializedEventType;
        ClrTypeName = clrTypeName;
    }

    public InvalidEventTypeException(
        string serializedEventType, string clrTypeName, string message, Exception inner) : base(message, inner)
    {
        SerializedEventType = serializedEventType;
        ClrTypeName = clrTypeName;
    }
    
    public string SerializedEventType { get; }
    public string ClrTypeName { get; }

    protected InvalidEventTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        SerializedEventType = (string)info.GetValue(nameof(SerializedEventType), typeof(string));
        ClrTypeName = (string)info.GetValue(nameof(ClrTypeName), typeof(string));
    }
    
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(SerializedEventType), SerializedEventType);
        info.AddValue(nameof(ClrTypeName), ClrTypeName);
    }
}