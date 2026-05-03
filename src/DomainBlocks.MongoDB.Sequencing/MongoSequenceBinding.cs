using MongoDB.Driver;

namespace DomainBlocks.MongoDB.Sequencing;

public sealed class MongoSequenceBinding<TDocument>(
    CollectionNamespace sequenceCollectionNamespace,
    CollectionNamespace targetCollectionNamespace,
    string sequenceId,
    FieldDefinition<TDocument, long> targetField)
{
    public CollectionNamespace SequenceCollectionNamespace { get; } = sequenceCollectionNamespace;
    public CollectionNamespace TargetCollectionNamespace { get; } = targetCollectionNamespace;
    public string SequenceId { get; } = sequenceId;
    public FieldDefinition<TDocument, long> TargetField { get; } = targetField;
}