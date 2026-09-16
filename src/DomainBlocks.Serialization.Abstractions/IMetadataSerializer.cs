namespace DomainBlocks.Serialization.Abstractions;

/// <summary>
/// Serializes flat string metadata to, and deserializes it from, a data representation of type
/// <typeparamref name="TData"/>.
/// </summary>
public interface IMetadataSerializer<TData>
{
    TData Serialize(ReadOnlySpan<KeyValuePair<string, string>> metadata);

    IReadOnlyDictionary<string, string> Deserialize(TData data);
}