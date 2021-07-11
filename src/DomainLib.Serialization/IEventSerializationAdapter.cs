using System;

namespace DomainLib.Serialization
{
    public interface IEventSerializationAdapter<TRawData>
    {
        ReadOnlyMemory<byte> FromRawData(TRawData rawEventData);
        TRawData ToRawData(ReadOnlyMemory<byte> bytes);
    }
}