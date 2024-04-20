using DomainBlocks.V1.Abstractions.Subscriptions.Messages;

namespace DomainBlocks.V1.Subscriptions.Messages;

public sealed class FlushRequested : ISubscriptionMessage
{
    private readonly TaskCompletionSource _taskCompletionSource = new();

    public Task WaitForReceiptAsync(CancellationToken cancellationToken = default)
    {
        return _taskCompletionSource.Task.WaitAsync(cancellationToken);
    }

    public void NotifyReceived()
    {
        _taskCompletionSource.SetResult();
    }
}