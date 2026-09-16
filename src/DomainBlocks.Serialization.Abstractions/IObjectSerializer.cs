namespace DomainBlocks.Serialization.Abstractions;

/// <summary>
/// Serializes objects to, and deserializes them from, a data representation of type <typeparamref name="TData"/>.
/// </summary>
public interface IObjectSerializer<TData>
{
    TData Serialize(object value);

    object Deserialize(TData data, Type type);
}