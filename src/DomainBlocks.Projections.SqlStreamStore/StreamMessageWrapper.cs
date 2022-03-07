using System;
using SqlStreamStore.Streams;

namespace DomainBlocks.Projections.SqlStreamStore
{
    public class StreamMessageWrapper
    {
        public StreamMessageWrapper(StreamMessage streamMessage, string materializedJsonData)
        {
            Position = streamMessage.Position;
            CreatedUtc = streamMessage.CreatedUtc;
            MessageId = streamMessage.MessageId;
            JsonMetadata = streamMessage.JsonMetadata;
            StreamVersion = streamMessage.StreamVersion;
            StreamId = streamMessage.StreamId;
            Type = streamMessage.Type;
            JsonData = materializedJsonData;
        }

        public readonly long Position;
        public DateTime CreatedUtc { get; }
        public Guid MessageId { get; }
        public string JsonMetadata { get; }
        public int StreamVersion { get; }
        public string StreamId { get; }
        public string Type { get; }
        public string JsonData { get;  }
    }
}