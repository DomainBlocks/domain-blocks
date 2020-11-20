using System;
using System.Runtime.Serialization;

namespace DomainLib.Serialization
{
    [Serializable]
    public class InvalidEventTypeException : EventDeserializeException
    {
        public string SerializedEventType { get; }
        public string ClrTypeName { get; }

        public InvalidEventTypeException(string serializedEventType, string clrTypeName)
        {
            SerializedEventType = serializedEventType;
            ClrTypeName = clrTypeName;
        }

        public InvalidEventTypeException(string message, string serializedEventType, string clrTypeName) 
            : base(message)
        {
            SerializedEventType = serializedEventType;
            ClrTypeName = clrTypeName;
        }

        public InvalidEventTypeException(string message, string serializedEventType, string clrTypeName, Exception inner) 
            : base(message, inner)
        {
            SerializedEventType = serializedEventType;
            ClrTypeName = clrTypeName;
        }

        protected InvalidEventTypeException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(ClrTypeName), ClrTypeName);
            info.AddValue(nameof(SerializedEventType), SerializedEventType);

            base.GetObjectData(info, context);
        }
    }
}