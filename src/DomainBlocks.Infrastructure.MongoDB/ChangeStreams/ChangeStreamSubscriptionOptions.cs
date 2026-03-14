using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public class ChangeStreamSubscriptionOptions
{
    public ChangeStreamOptions MongoOptions { get; set; } = new();

    public int QueueSize { get; set; } = 1_000;

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    public int MaxRetryAttempts { get; set; } = int.MaxValue;
}