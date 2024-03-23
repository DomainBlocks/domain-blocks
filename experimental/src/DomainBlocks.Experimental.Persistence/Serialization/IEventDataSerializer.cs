namespace DomainBlocks.Experimental.Persistence.Serialization;

public interface IEventDataSerializer
{
    SerializationFormat Format { get; }
    byte[] Serialize(object value);
    object Deserialize(ReadOnlySpan<byte> data, Type type);
}