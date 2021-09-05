using System;
using System.Runtime.Serialization;

namespace DomainBlocks.Persistence
{
    [Serializable]
    public class StreamNotFoundException : Exception
    {
        public string StreamName { get; }

        public StreamNotFoundException(string streamName)
        {
            StreamName = streamName;
        }

        public StreamNotFoundException(string streamName, string message) : base(message)
        {
            StreamName = streamName;
        }

        public StreamNotFoundException(string streamName, string message, Exception inner) : base(message, inner)
        {
            StreamName = streamName;
        }

        protected StreamNotFoundException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            info.AddValue(nameof(StreamName), StreamName);

            base.GetObjectData(info, context);
        }
    }
}