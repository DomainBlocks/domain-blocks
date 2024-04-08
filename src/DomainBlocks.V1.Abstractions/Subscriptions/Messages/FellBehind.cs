namespace DomainBlocks.V1.Abstractions.Subscriptions.Messages;

public class FellBehind : ISubscriptionStatusMessage
{
    public static readonly FellBehind Instance = new();
}