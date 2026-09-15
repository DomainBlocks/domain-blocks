namespace DomainBlocks.Serialization.Abstractions;

/// <summary>
/// A metadata serializer over UTF-8 or binary bytes. Implementations serialize to a byte array and deserialize from
/// a span, which lets one implementation serve both <see cref="T:byte[]"/> and <see cref="ReadOnlyMemory{T}"/> data
/// without copying.
/// </summary>
public interface IByteMetadataSerializer : IMetadataSerializer<byte[]>, IMetadataSerializer<ReadOnlyMemory<byte>>
{
    IReadOnlyDictionary<string, string> Deserialize(ReadOnlySpan<byte> data);

    IReadOnlyDictionary<string, string> IMetadataSerializer<byte[]>.Deserialize(byte[] data) =>
        Deserialize(data.AsSpan());

    IReadOnlyDictionary<string, string> IMetadataSerializer<ReadOnlyMemory<byte>>.Deserialize(
        ReadOnlyMemory<byte> data) => Deserialize(data.Span);

    ReadOnlyMemory<byte> IMetadataSerializer<ReadOnlyMemory<byte>>.Serialize(
        ReadOnlySpan<KeyValuePair<string, string>> metadata) =>
        ((IMetadataSerializer<byte[]>)this).Serialize(metadata);
}