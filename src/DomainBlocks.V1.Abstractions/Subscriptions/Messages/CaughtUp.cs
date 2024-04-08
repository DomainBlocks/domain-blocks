namespace DomainBlocks.V1.Abstractions.Subscriptions.Messages;

public class CaughtUp : ISubscriptionStatusMessage
{
    public static readonly CaughtUp Instance = new();
}