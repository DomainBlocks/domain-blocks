using MongoDB.Driver;

namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Binds a target collection to a sequence counter collection, defining where documents are inserted and in which field
/// each sequence number is stored.
/// </summary>
public sealed class MongoSequenceBinding<TDocument>(
    CollectionNamespace sequenceCollectionNamespace,
    CollectionNamespace targetCollectionNamespace,
    string sequenceId,
    FieldDefinition<TDocument, long> targetField)
{
    /// <summary>
    /// The namespace of the collection used to store and increment the sequence counter.
    /// </summary>
    public CollectionNamespace SequenceCollectionNamespace { get; } = sequenceCollectionNamespace;

    /// <summary>
    /// The namespace of the target collection into which documents are appended.
    /// </summary>
    public CollectionNamespace TargetCollectionNamespace { get; } = targetCollectionNamespace;

    /// <summary>
    /// The identifier of the sequence counter within the sequence collection. Allows multiple independent sequences to
    /// share the same sequence collection.
    /// </summary>
    public string SequenceId { get; } = sequenceId;

    /// <summary>
    /// The field on the target document where its sequence number is written.
    /// </summary>
    public FieldDefinition<TDocument, long> TargetField { get; } = targetField;
}