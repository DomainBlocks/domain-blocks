namespace DomainBlocks.Serialization.Abstractions;

public interface IByteObjectDeserializer : IObjectDeserializer<byte[]>, IObjectDeserializer<ReadOnlyMemory<byte>>
{
    object Deserialize(ReadOnlySpan<byte> value, Type type);

    object IObjectDeserializer<byte[]>.Deserialize(byte[] value, Type type) => Deserialize(value.AsSpan(), type);

    object IObjectDeserializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> value, Type type) =>
        Deserialize(value.Span, type);
}