namespace DomainBlocks.Serialization.Abstractions;

/// <summary>
/// Defines methods that serialize objects to and from <typeparamref name="TData"/>.
/// </summary>
public interface IObjectSerializer<TData>
{
    TData Serialize(object value);

    object Deserialize(TData data, Type type);
}