namespace DomainBlocks.Serialization.Abstractions;

public interface IMetadataSerde<TData> : IMetadataSerializer<TData>, IMetadataDeserializer<TData>;