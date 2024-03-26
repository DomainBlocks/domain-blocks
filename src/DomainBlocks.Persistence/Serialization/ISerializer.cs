namespace DomainBlocks.Persistence.Serialization;

public interface ISerializer
{
    SerializationFormat Format { get; }
    byte[] Serialize(object value);
    object Deserialize(ReadOnlySpan<byte> data, Type type);
}