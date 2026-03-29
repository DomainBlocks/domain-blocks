using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Serialization;

public static partial class BsonExtensions
{
    private const string KindFieldName = "kind";
    private const string VersionFieldName = "version";
    private const string Any = "any";
    private const string StreamExists = "streamExists";
    private const string StreamDoesNotExist = "streamDoesNotExist";
    private const string Version = "version";

    extension(BsonDocument doc)
    {
        public static BsonDocument From(ExpectedStreamState value)
        {
            return value.Kind switch
            {
                ExpectedStreamStateKind.Any => new BsonDocument(KindFieldName, Any),
                ExpectedStreamStateKind.StreamExists => new BsonDocument(KindFieldName, StreamExists),
                ExpectedStreamStateKind.StreamDoesNotExist => new BsonDocument(KindFieldName, StreamDoesNotExist),

                ExpectedStreamStateKind.SpecificVersion => new BsonDocument
                {
                    { KindFieldName, Version },
                    { VersionFieldName, checked((long)value.Version!.Value.Value) }
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

            return doc[KindFieldName].AsString switch
            {
                Any => ExpectedStreamState.Any,
                StreamExists => ExpectedStreamState.StreamExists,
                StreamDoesNotExist => ExpectedStreamState.StreamDoesNotExist,
                Version => ExpectedStreamState.SpecificVersion(StreamVersion.FromInt64(doc[VersionFieldName].AsInt64)),
                var k => throw new ArgumentOutOfRangeException(nameof(doc), $"Unknown kind: '{k}'")
            };
        }
    }
}