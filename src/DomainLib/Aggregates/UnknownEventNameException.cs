using System;
using System.Runtime.Serialization;

namespace DomainLib.Aggregates
{
    [Serializable]
    public class UnknownEventNameException : Exception
    {
        public string EventName { get; }

        public UnknownEventNameException(string eventName)
        {
            EventName = eventName;
        }

        public UnknownEventNameException(string message, string eventName) : base(message)
        {
            EventName = eventName;
        }

        public UnknownEventNameException(string message, string eventName, Exception inner) : base(message, inner)
        {
            EventName = eventName;
        }

        protected UnknownEventNameException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(EventName), EventName);

            base.GetObjectData(info, context);
        }
    }
}