using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using static DomainBlocks.EventStore.MongoDB.Schema.ExpectedStreamStateSchema;

namespace DomainBlocks.EventStore.MongoDB.Serialization;

internal static partial class BsonExtensions
{
    extension(BsonDocument doc)
    {
        public static BsonDocument From(ExpectedStreamState value)
        {
            return value.Kind switch
            {
                ExpectedStreamStateKind.Any => new BsonDocument(FieldNames.Kind, Kinds.Any),

                ExpectedStreamStateKind.StreamExists => new BsonDocument(FieldNames.Kind, Kinds.StreamExists),

                ExpectedStreamStateKind.StreamDoesNotExist =>
                    new BsonDocument(FieldNames.Kind, Kinds.StreamDoesNotExist),

                ExpectedStreamStateKind.SpecificVersion => new BsonDocument
                {
                    { FieldNames.Kind, Kinds.SpecificVersion },
                    { FieldNames.Version, checked((long)value.Version!.Value.Value) }
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

            return doc[FieldNames.Kind].AsString switch
            {
                Kinds.Any => ExpectedStreamState.Any,

                Kinds.StreamExists => ExpectedStreamState.StreamExists,

                Kinds.StreamDoesNotExist => ExpectedStreamState.StreamDoesNotExist,

                Kinds.SpecificVersion =>
                    ExpectedStreamState.SpecificVersion(StreamVersion.FromInt64(doc[FieldNames.Version].AsInt64)),

                var k => throw new ArgumentOutOfRangeException(nameof(doc), $"Unknown kind: {k}")
            };
        }
    }
}