namespace DomainBlocks.V1.Abstractions.Subscriptions.Messages;

public class SubscriptionDropped : ISubscriptionStatusMessage
{
    public SubscriptionDropped(Exception? exception)
    {
        Exception = exception;
    }

    public Exception? Exception { get; }
}