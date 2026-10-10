namespace DomainBlocks.EventStore.Metadata;

/// <summary>
/// Defines a component that adds metadata to events appended through the store. Metadata set on the
/// <see cref="AppendableEvent{TPayload}"/> takes precedence.
/// </summary>
public interface IMetadataContributor<in TEvent> where TEvent : notnull
{
    void Contribute(TEvent @event, MetadataWriter metadata);
}