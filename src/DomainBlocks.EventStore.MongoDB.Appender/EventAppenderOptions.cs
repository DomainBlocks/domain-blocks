using System.ComponentModel.DataAnnotations;

namespace DomainBlocks.EventStore.MongoDB.Appender;

public sealed class EventAppenderOptions
{
    public const string SectionName = "EventAppender";

    [Range(1, 100_000)]
    public int QueueSize { get; init; } = 10_000;

    public TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromSeconds(2);

    [Required]
    public MongoOptions Mongo { get; init; } = new();
}