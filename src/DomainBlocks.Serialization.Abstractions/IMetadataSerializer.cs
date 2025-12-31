namespace DomainBlocks.Serialization.Abstractions;

public interface IMetadataSerializer<TData> where TData : notnull
{
    TData Serialize(IReadOnlyDictionary<string, string> metadata);

    IReadOnlyDictionary<string, string> Deserialize(TData metadata);
}