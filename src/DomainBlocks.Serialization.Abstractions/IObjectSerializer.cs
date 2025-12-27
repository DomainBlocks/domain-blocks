namespace DomainBlocks.Serialization.Abstractions;

public interface IObjectSerializer<TSerialized>
{
    TSerialized Serialize(object value);

    object Deserialize(TSerialized value, Type type);
}