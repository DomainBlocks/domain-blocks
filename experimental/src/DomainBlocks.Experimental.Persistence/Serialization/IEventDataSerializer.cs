namespace DomainBlocks.Experimental.Persistence.Serialization;

public interface IEventDataSerializer<TRawData>
{
    string ContentType { get; }
    TRawData Serialize(object obj);
    object Deserialize(TRawData data, Type type);
    T Deserialize<T>(TRawData data);
}