using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Bson;
using static DomainBlocks.EventStore.MongoDB.Schema.ExpectedStreamStateSchema;

namespace DomainBlocks.EventStore.MongoDB.Serialization;

public static partial class BsonExtensions
{
    extension(BsonDocument doc)
    {
        public static BsonDocument From(ExpectedStreamState value)
        {
            return value.Kind switch
            {
                ExpectedStreamStateKind.Any => new BsonDocument(ExpectedStreamStateSchema.FieldNames.Kind, ExpectedStreamStateSchema.Kinds.Any),

                ExpectedStreamStateKind.StreamExists => new BsonDocument(ExpectedStreamStateSchema.FieldNames.Kind, ExpectedStreamStateSchema.Kinds.StreamExists),

                ExpectedStreamStateKind.StreamDoesNotExist =>
                    new BsonDocument(ExpectedStreamStateSchema.FieldNames.Kind, ExpectedStreamStateSchema.Kinds.StreamDoesNotExist),

                ExpectedStreamStateKind.SpecificVersion => new BsonDocument
                {
                    { ExpectedStreamStateSchema.FieldNames.Kind, ExpectedStreamStateSchema.Kinds.SpecificVersion },
                    { ExpectedStreamStateSchema.FieldNames.Version, checked((long)value.Version!.Value.Value) }
                },

                _ => throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Unknown {nameof(ExpectedStreamStateKind)}: {value.Kind}")
            };
        }
    }

    extension(BsonValue value)
    {
        public ExpectedStreamState ToExpectedStreamState()
        {
            var doc = value.AsBsonDocument;

            return doc[ExpectedStreamStateSchema.FieldNames.Kind].AsString switch
            {
                ExpectedStreamStateSchema.Kinds.Any => ExpectedStreamState.Any,

                ExpectedStreamStateSchema.Kinds.StreamExists => ExpectedStreamState.StreamExists,

                ExpectedStreamStateSchema.Kinds.StreamDoesNotExist => ExpectedStreamState.StreamDoesNotExist,

                ExpectedStreamStateSchema.Kinds.SpecificVersion =>
                    ExpectedStreamState.SpecificVersion(StreamVersion.FromInt64(doc[ExpectedStreamStateSchema.FieldNames.Version].AsInt64)),

                var k => throw new ArgumentOutOfRangeException(nameof(doc), $"Unknown kind: {k}")
            };
        }
    }
}