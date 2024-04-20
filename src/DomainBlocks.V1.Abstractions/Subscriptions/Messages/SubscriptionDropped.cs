namespace DomainBlocks.V1.Abstractions.Subscriptions.Messages;

public sealed class SubscriptionDropped : ISubscriptionStatusMessage
{
    public SubscriptionDropped(Exception? exception)
    {
        Exception = exception;
    }

    public Exception? Exception { get; }
}