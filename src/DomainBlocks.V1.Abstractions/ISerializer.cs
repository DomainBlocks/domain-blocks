namespace DomainBlocks.V1.Abstractions;

public interface ISerializer
{
    byte[] Serialize(object value);
    object Deserialize(ReadOnlySpan<byte> data, Type type);
}