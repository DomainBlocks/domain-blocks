namespace DomainBlocks.Persistence.Serialization;

public interface ISerializer
{
    byte[] Serialize(object value);
    object Deserialize(ReadOnlySpan<byte> data, Type type);
}