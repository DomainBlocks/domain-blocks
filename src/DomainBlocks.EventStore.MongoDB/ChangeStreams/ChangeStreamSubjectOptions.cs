using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal class ChangeStreamSubjectOptions
{
    public static readonly ChangeStreamSubjectOptions Default = new();

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