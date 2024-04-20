namespace DomainBlocks.V1.Abstractions.Subscriptions.Messages;

public sealed class FellBehind : ISubscriptionStatusMessage
{
    public static readonly FellBehind Instance = new();
}