namespace DomainBlocks.Serialization.Abstractions;

public interface IObjectSerializer<out TData>
{
    TData Serialize(object value);
}