using DomainBlocks.V1.Abstractions.Subscriptions.Messages;

namespace DomainBlocks.V1.Subscriptions.Messages;

public sealed class TimerTicked : ISubscriptionMessage
{
    public static readonly TimerTicked Instance = new();
}