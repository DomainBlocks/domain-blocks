using System.ComponentModel.DataAnnotations;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

public sealed class EventAppenderOptions
{
    public const string SectionName = "EventAppender";

    [Range(1, 100_000)]
    public int QueueSize { get; init; } = 10_000;

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    [Required]
    public MongoOptions Mongo { get; init; } = new();
}