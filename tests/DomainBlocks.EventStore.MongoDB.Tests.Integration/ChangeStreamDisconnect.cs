namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public sealed class ChangeStreamDisconnect
{
    private readonly TaskCompletionSource _resumptionAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _resumptionRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _isDisconnected;


    public Task ResumptionAttempt => _resumptionAttempt.Task;

    public Task ResumptionRelease => _resumptionRelease.Task;

    public void SignalResumptionAttempt() => _resumptionAttempt.TrySetResult();

    public void FailResumption(Exception exception) => _resumptionRelease.TrySetException(exception);

    // A timeout is an error that the change stream resumes from. Only the first batch after the request fails.
    public Exception? TakeDisconnectError() =>
        Interlocked.Exchange(ref _isDisconnected, 1) == 0 ? new TimeoutException("Disconnected.") : null;
}