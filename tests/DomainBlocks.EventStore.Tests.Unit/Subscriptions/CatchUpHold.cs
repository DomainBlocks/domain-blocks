namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// Holds a catch-up at a point until it is released.
/// </summary>
internal sealed class CatchUpHold
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when the catch-up reaches the hold.
    /// </summary>
    public Task Reached => _reached.Task;

    public void Release() => _released.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        _reached.TrySetResult();
        return _released.Task.WaitAsync(cancellationToken);
    }
}