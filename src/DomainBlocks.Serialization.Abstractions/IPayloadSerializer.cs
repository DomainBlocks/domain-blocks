namespace DomainBlocks.Serialization.Abstractions;

public interface IPayloadSerializer<TPayload>
{
    TPayload Serialize(object value);

    object Deserialize(TPayload payload, Type type);
}