namespace DomainBlocks.EventStore;

public interface IMetadataContributor<in TEventBase> where TEventBase : class
{
    void Contribute(TEventBase @event, object? contract, string eventName, MetadataWriter metadata);
}