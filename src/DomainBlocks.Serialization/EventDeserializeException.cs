using System;
using System.Runtime.Serialization;

namespace DomainBlocks.Serialization
{
    [Serializable]
    public class EventDeserializeException : Exception
    {
        public EventDeserializeException()
        {
        }

        public EventDeserializeException(string message) : base(message)
        {
        }

        public EventDeserializeException(string message, Exception inner) : base(message, inner)
        {
        }

        protected EventDeserializeException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }
}