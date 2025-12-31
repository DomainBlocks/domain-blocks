namespace DomainBlocks.Serialization.Abstractions;

public interface IObjectSerializer<TData>
{
    TData Serialize(object value);

    object Deserialize(TData value, Type type);
}