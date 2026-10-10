namespace DomainBlocks.Serialization.Abstractions;

/// <summary>
/// Defines an object serializer for UTF-8 or binary data that serves both <see cref="T:byte[]"/> and
/// <see cref="ReadOnlyMemory{T}"/> without copying.
/// </summary>
public interface IByteObjectSerializer : IObjectSerializer<byte[]>, IObjectSerializer<ReadOnlyMemory<byte>>
{
    object Deserialize(ReadOnlySpan<byte> data, Type type);

    object IObjectSerializer<byte[]>.Deserialize(byte[] data, Type type) => Deserialize(data.AsSpan(), type);

    ReadOnlyMemory<byte> IObjectSerializer<ReadOnlyMemory<byte>>.Serialize(object value) =>
        ((IObjectSerializer<byte[]>)this).Serialize(value);

    object IObjectSerializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> data, Type type) =>
        Deserialize(data.Span, type);
}