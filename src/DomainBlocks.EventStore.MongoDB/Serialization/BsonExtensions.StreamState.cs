using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using static DomainBlocks.EventStore.MongoDB.Schema.StreamStateSchema;

namespace DomainBlocks.EventStore.MongoDB.Serialization;

internal static partial class BsonExtensions
{
    extension(BsonDocument doc)
    {
        public static BsonDocument From(StreamState value)
        {
            return value.Kind switch
            {
                StreamStateKind.StreamDoesNotExist => new BsonDocument(FieldNames.Kind, Kinds.StreamDoesNotExist),

                StreamStateKind.StreamExists => new BsonDocument
                {
                    { FieldNames.Kind, Kinds.StreamExists },
                    { FieldNames.Version, checked((long)value.Version!.Value.Value) }
                },

                _ => throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Unknown {nameof(StreamStateKind)}: {value.Kind}")
            };
        }
    }

    extension(BsonValue value)
    {
        public StreamState ToStreamState()
        {
            var doc = value.AsBsonDocument;

            return doc[FieldNames.Kind].AsString switch
            {
                Kinds.StreamDoesNotExist => StreamState.StreamDoesNotExist,

                Kinds.StreamExists =>
                    StreamState.StreamExists(StreamVersion.FromInt64(doc[FieldNames.Version].AsInt64)),

                var k => throw new ArgumentOutOfRangeException(nameof(doc), $"Unknown kind: {k}")
            };
        }
    }
}