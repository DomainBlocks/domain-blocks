namespace DomainBlocks.Serialization.Abstractions;

public interface IByteMetadataDeserializer : IMetadataDeserializer<byte[]>, IMetadataDeserializer<ReadOnlyMemory<byte>>
{
    IReadOnlyDictionary<string, string> Deserialize(ReadOnlySpan<byte> metadata);

    IReadOnlyDictionary<string, string> IMetadataDeserializer<byte[]>.Deserialize(byte[] metadata) =>
        Deserialize(metadata.AsSpan());

    IReadOnlyDictionary<string, string> IMetadataDeserializer<ReadOnlyMemory<byte>>.Deserialize(
        ReadOnlyMemory<byte> matadata) => Deserialize(matadata.Span);
}