using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client;

public static class SystemEvents
{
    private static readonly string EventNamePrefix = "$dbx.sys";

    public sealed class CommitRequested
    {
        public static readonly string Name = $"{EventNamePrefix}.CommitRequested";

        public required int EventCount { get; init; }

        [BsonSerializer(typeof(ExpectedStreamStateBsonSerializer))]
        public required ExpectedStreamState ExpectedStreamState { get; init; }
    }

    public sealed class LeaderElected
    {
        public static readonly string Name = $"{EventNamePrefix}.LeaderElected";

        public required string HolderId { get; init; }
        public required long Epoch { get; init; }
        public required long GlobalPosition { get; init; }
    }

    public sealed class CommitAccepted
    {
        public static readonly string Name = $"{EventNamePrefix}.CommitAccepted";

        public required long Epoch { get; init; }
        public required long StartStreamVersion { get; init; }
        public required long StartGlobalPosition { get; init; }
        public required int EventCount { get; init; }
    }

    public sealed class CommitRejected
    {
        public static readonly string Name = $"{EventNamePrefix}.CommitRejected";

        public required long Epoch { get; init; }
    }
}