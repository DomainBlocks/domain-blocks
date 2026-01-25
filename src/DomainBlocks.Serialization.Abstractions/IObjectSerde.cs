namespace DomainBlocks.Serialization.Abstractions;

public interface IObjectSerde<TData> : IObjectSerializer<TData>, IObjectDeserializer<TData>;