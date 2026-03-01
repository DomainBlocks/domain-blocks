using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public sealed class LeaseState
{
    [BsonElement(FieldNames.CommitPosition)]
    public required long CommitPosition { get; init; }

    public static class FieldNames
    {
        public const string CommitPosition = "commitPosition";
    }
}