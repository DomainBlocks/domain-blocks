using System;

namespace DomainLib.Serialization
{
    public class ByteArrayEventSerializationAdapter : IEventSerializationAdapter<ReadOnlyMemory<byte>>
    {
        public ReadOnlyMemory<byte> FromRawData(ReadOnlyMemory<byte> rawEventData)
        {
            return rawEventData;
        }

        public ReadOnlyMemory<byte> ToRawData(ReadOnlyMemory<byte> bytes)
        {
            return bytes;
        }
    }
}