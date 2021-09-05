using System;

namespace DomainBlocks.Serialization
{
    public interface IEventSerializationAdapter<TRawData>
    {
        ReadOnlyMemory<byte> FromRawData(TRawData rawEventData);
        TRawData ToRawData(ReadOnlyMemory<byte> bytes);
    }
}