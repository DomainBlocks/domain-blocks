namespace DomainBlocks.Serialization.Abstractions;

public interface IMetadataSerializer<out TData>
{
    TData Serialize(IReadOnlyDictionary<string, string> metadata);
}