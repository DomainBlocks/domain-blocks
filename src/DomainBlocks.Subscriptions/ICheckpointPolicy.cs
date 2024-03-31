namespace DomainBlocks.Subscriptions;

public interface ICheckpointPolicy
{
    bool IsCheckpointRequired(int eventsProcessedSinceLastCheckpoint);
    bool IsCheckpointRequired(TimeSpan timeElapsedSinceLastCheckpoint);
    bool IsCheckpointRequired(object currentEvent);
}