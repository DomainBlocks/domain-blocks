using System;
using System.Runtime.Serialization;

namespace DomainBlocks.Persistence
{
    [Serializable]
    public class StreamDeletedException : Exception
    {
        public string StreamName { get; }

        public StreamDeletedException(string streamName)
        {
            StreamName = streamName;
        }

        public StreamDeletedException(string streamName, string message) : base(message)
        {
            StreamName = streamName;
        }

        public StreamDeletedException(string streamName, string message, Exception inner) : base(message, inner)
        {
            StreamName = streamName;
        }

        protected StreamDeletedException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            info.AddValue(nameof(StreamName), StreamName);

            base.GetObjectData(info, context);
        }
    }
}