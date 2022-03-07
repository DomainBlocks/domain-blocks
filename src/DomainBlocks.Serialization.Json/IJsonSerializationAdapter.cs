using System;
using System.Text.Json;

namespace DomainBlocks.Serialization.Json
{
    public interface IJsonSerializationAdapter<TRawData>
    {
        TRawData Serialize(object obj, JsonSerializerOptions options);

        object Deserialize(TRawData rawData, Type eventType, JsonSerializerOptions options);

        T Deserialize<T>(TRawData rawData, JsonSerializerOptions options);
    }
}