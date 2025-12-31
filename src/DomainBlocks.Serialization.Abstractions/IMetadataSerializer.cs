namespace DomainBlocks.Serialization.Abstractions;

public interface IMetadataSerializer<TSerialized> where TSerialized : notnull
{
    TSerialized Serialize(IReadOnlyDictionary<string, string> metadata);

    IReadOnlyDictionary<string, string> Deserialize(TSerialized metadata);
}