namespace DomainBlocks.Experimental.Persistence.Serialization;

public interface IEventDataSerializer
{
    byte[] SerializeToBytes(object value);
    string SerializeToString(object value);
    object Deserialize(ReadOnlySpan<byte> data, Type type);
    object Deserialize(string data, Type type);
}