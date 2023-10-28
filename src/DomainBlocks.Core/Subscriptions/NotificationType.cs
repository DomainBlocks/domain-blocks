namespace DomainBlocks.Core.Subscriptions;

internal enum NotificationType
{
    CatchingUp,
    Event,
    Live,
    SubscriptionDropped,
    CheckpointTimerElapsed
}