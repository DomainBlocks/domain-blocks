namespace DomainBlocks.Core.Subscriptions;

internal enum QueueNotificationType
{
    CatchingUp,
    Event,
    Live,
    SubscriptionDropped,
    CheckpointTimerElapsed
}