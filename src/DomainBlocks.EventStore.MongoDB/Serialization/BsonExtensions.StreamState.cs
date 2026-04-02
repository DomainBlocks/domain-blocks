using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Bson;
using static DomainBlocks.EventStore.MongoDB.Schema.StreamStateSchema;

namespace DomainBlocks.EventStore.MongoDB.Serialization;

public static partial class BsonExtensions
{
    extension(BsonDocument doc)
    {
        public static BsonDocument From(StreamState value)
        {
            return value.Kind switch
            {
                StreamStateKind.StreamDoesNotExist => new BsonDocument(StreamStateSchema.FieldNames.Kind, StreamStateSchema.Kinds.StreamDoesNotExist),

                StreamStateKind.StreamExists => new BsonDocument
                {
                    { StreamStateSchema.FieldNames.Kind, StreamStateSchema.Kinds.StreamExists },
                    { StreamStateSchema.FieldNames.Version, checked((long)value.Version!.Value.Value) }
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

            return doc[StreamStateSchema.FieldNames.Kind].AsString switch
            {
                StreamStateSchema.Kinds.StreamDoesNotExist => StreamState.StreamDoesNotExist,

                StreamStateSchema.Kinds.StreamExists =>
                    StreamState.StreamExists(StreamVersion.FromInt64(doc[StreamStateSchema.FieldNames.Version].AsInt64)),

                var k => throw new ArgumentOutOfRangeException(nameof(doc), $"Unknown kind: {k}")
            };
        }
    }
}