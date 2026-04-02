using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Schema;

// ReSharper disable all
public sealed class CommitRejection
{
    [BsonElement(FieldNames.CommitId)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid CommitId { get; init; }

    [BsonElement(FieldNames.StreamId)]
    public required string StreamId { get; init; }

    [BsonElement(FieldNames.ExpectedStreamState)]
    [BsonSerializer(typeof(ExpectedStreamStateBsonSerializer))]
    public required ExpectedStreamState ExpectedStreamState { get; init; }

    [BsonElement(FieldNames.ActualStreamState)]
    [BsonSerializer(typeof(StreamStateBsonSerializer))]
    public required StreamState ActualStreamState { get; init; }

    public static class FieldNames
    {
        public const string CommitId = "commitId";
        public const string StreamId = "streamId";
        public const string ExpectedStreamState = "expectedStreamState";
        public const string ActualStreamState = "actualStreamState";
    }
}