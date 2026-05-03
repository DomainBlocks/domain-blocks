namespace DomainBlocks.MongoDB.Sequencing;

public class MongoSequencedAppenderOptions
{
    public int QueueCapacity { get; set; } = 1_000;
    public int BatchSize { get; set; } = 500;
}