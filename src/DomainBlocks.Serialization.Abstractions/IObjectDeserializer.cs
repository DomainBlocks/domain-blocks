namespace DomainBlocks.Serialization.Abstractions;

public interface IObjectDeserializer<in TData>
{
    object Deserialize(TData value, Type type);
}