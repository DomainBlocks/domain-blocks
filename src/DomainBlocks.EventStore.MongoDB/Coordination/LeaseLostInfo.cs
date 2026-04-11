namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class LeaseLostInfo(LeaseLostReason reason, Exception? exception = null)
{
    public LeaseLostReason Reason { get; } = reason;
    public Exception? Exception { get; } = exception;
}