namespace DomainBlocks.EventStore.Metadata;

/// <summary>
/// Adds metadata to every event appended through a store pipeline, before the event reaches the store. Entries
/// supplied explicitly on the <see cref="AppendableEvent{TPayload}"/> take precedence over contributed
/// ones.
/// </summary>
public interface IMetadataContributor<in TEvent> where TEvent : notnull
{
    void Contribute(TEvent @event, MetadataWriter metadata);
}