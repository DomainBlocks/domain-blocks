using System;

namespace DomainBlocks.Core.Serialization;

public interface IEventDataSerializer<TRawData>
{
    string ContentType { get; }
    TRawData Serialize(object obj);
    object Deserialize(TRawData data, Type type);
}