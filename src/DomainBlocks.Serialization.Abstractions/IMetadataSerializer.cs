namespace DomainBlocks.Serialization.Abstractions;

public interface IMetadataSerializer<TData>
{
    TData Serialize(IReadOnlyDictionary<string, string> metadata);

    IReadOnlyDictionary<string, string> Deserialize(TData metadata);
}