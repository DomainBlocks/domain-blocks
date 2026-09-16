namespace DomainBlocks.Serialization.Abstractions;

/// <summary>
/// An object serializer over UTF-8 or binary bytes. Implementations serialize to a byte array and deserialize from a
/// span, which lets one implementation serve both <see cref="T:byte[]"/> and <see cref="ReadOnlyMemory{T}"/> data
/// without copying.
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