using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Serialization;

public static partial class BsonExtensions
{
    extension(BsonDocument doc)
    {
        public static BsonDocument From(StreamState value)
        {
            return value.Kind switch
            {
                StreamStateKind.StreamDoesNotExist => new BsonDocument(KindFieldName, StreamDoesNotExist),

                StreamStateKind.StreamExists => new BsonDocument
                {
                    { KindFieldName, StreamExists },
                    { VersionFieldName, checked((long)value.Version!.Value.Value) }
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

            return doc[KindFieldName].AsString switch
            {
                StreamDoesNotExist => StreamState.StreamDoesNotExist,
                StreamExists => StreamState.StreamExists(StreamVersion.FromInt64(doc[VersionFieldName].AsInt64)),
                var k => throw new ArgumentOutOfRangeException(nameof(doc), $"Unknown kind: '{k}'")
            };
        }
    }
}