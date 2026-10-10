namespace DomainBlocks.Serialization.Abstractions;

/// <summary>
/// Defines methods that serialize flat string metadata to and from <typeparamref name="TData"/>.
/// </summary>
public interface IMetadataSerializer<TData>
{
    TData Serialize(ReadOnlySpan<KeyValuePair<string, string>> metadata);

    IReadOnlyDictionary<string, string> Deserialize(TData data);
}