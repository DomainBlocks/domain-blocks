using DomainBlocks.V1.Abstractions.Subscriptions.Messages;

namespace DomainBlocks.V1.Subscriptions.Messages;

public class ResumeRequested : ISubscriptionMessage
{
    public static readonly ResumeRequested Instance = new();
}