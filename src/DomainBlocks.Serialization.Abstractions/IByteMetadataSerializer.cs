namespace DomainBlocks.Serialization.Abstractions;

/// <summary>
/// Defines a metadata serializer for UTF-8 or binary data that serves both <see cref="T:byte[]"/> and
/// <see cref="ReadOnlyMemory{T}"/> without copying.
/// </summary>
public interface IByteMetadataSerializer : IMetadataSerializer<byte[]>, IMetadataSerializer<ReadOnlyMemory<byte>>
{
    IReadOnlyDictionary<string, string> Deserialize(ReadOnlySpan<byte> data);

    IReadOnlyDictionary<string, string> IMetadataSerializer<byte[]>.Deserialize(byte[] data) =>
        Deserialize(data.AsSpan());

    ReadOnlyMemory<byte> IMetadataSerializer<ReadOnlyMemory<byte>>.Serialize(
        ReadOnlySpan<KeyValuePair<string, string>> metadata) => ((IMetadataSerializer<byte[]>)this).Serialize(metadata);

    IReadOnlyDictionary<string, string> IMetadataSerializer<ReadOnlyMemory<byte>>.Deserialize(
        ReadOnlyMemory<byte> data) => Deserialize(data.Span);
}