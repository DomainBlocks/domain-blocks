namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal class ChangeStreamSubjectOptions
{
    public static readonly ChangeStreamSubjectOptions Default = new();

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    public int MaxRetryAttempts { get; set; } = int.MaxValue;
}