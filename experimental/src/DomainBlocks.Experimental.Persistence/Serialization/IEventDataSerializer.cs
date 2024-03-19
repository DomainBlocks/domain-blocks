namespace DomainBlocks.Experimental.Persistence.Serialization;

public interface IEventDataSerializer<TSerializedData>
{
    TSerializedData Serialize(object value);
    object Deserialize(TSerializedData data, Type type);
}