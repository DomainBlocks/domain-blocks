namespace DomainBlocks.MongoDB.Sequencing;

public class MongoSequencedAppenderOptions
{
    public int QueueCapacity { get; set; } = 1_000;
    public int BatchSize { get; set; } = 500;
    public int MaxConflictRetries { get; set; } = 3;
    public TimeSpan ConflictRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);
}