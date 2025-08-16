namespace DomainBlocks.Serialization.Abstractions;

public interface ISerializer<TPayload>
{
    TPayload Serialize(object value);

    object? Deserialize(TPayload payload, Type type);
}