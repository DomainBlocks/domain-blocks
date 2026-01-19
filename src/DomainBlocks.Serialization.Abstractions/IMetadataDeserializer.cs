namespace DomainBlocks.Serialization.Abstractions;

public interface IMetadataDeserializer<in TData>
{
    IReadOnlyDictionary<string, string> Deserialize(TData metadata);
}