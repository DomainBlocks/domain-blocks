using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public class ChangeStreamSubjectOptions
{
    public ChangeStreamOptions MongoOptions { get; set; } = new();

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    public int MaxRetryAttempts { get; set; } = int.MaxValue;

    public ChangeStreamSubjectOptions Copy()
    {
        return new ChangeStreamSubjectOptions
        {
            MongoOptions = MongoOptions.Copy(),
            MaxRetryDelay = MaxRetryDelay,
            MaxRetryAttempts = MaxRetryAttempts
        };
    }
}