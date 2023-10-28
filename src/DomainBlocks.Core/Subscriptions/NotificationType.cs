namespace DomainBlocks.Core.Subscriptions;

public enum NotificationType
{
    CatchingUp,
    Event,
    Live,
    SubscriptionDropped,
    CheckpointTimerElapsed
}