namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Appends a few events at a time to random streams, one operation at a time, and retries each failed attempt.
/// </summary>
internal sealed class ChaosWriter(
    IEventStore<object, string, StreamPosition, LogPosition> eventStore,
    RetryCommitId retryCommitId,
    int seed)
{
    private const int StreamCount = 20;
    private const int MaxEventsPerAppend = 5;
    private const int MaxAttempts = 30;

    // Shorter than a fault, so an append in flight when a fault starts times out while the fault lasts.
    private static readonly AppendOptions AppendOptions = new() { Timeout = TimeSpan.FromSeconds(1) };

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly Random _random = new(seed);

    /// <summary>
    /// Runs operations until <paramref name="stopping"/> is canceled, and lets the operation in progress end first.
    /// </summary>
    public async Task<IReadOnlyList<AppendOperation>> RunAsync(
        CancellationToken stopping,
        CancellationToken cancellationToken)
    {
        var operations = new List<AppendOperation>();

        while (!stopping.IsCancellationRequested)
            operations.Add(await AppendAsync(operations.Count, cancellationToken));

        return operations;
    }

    private async Task<AppendOperation> AppendAsync(long number, CancellationToken cancellationToken)
    {
        var streamId = $"stream-{_random.Next(StreamCount)}";

        AppendableEvent<object>[] events =
        [
            .. Enumerable
                .Range(0, _random.Next(1, MaxEventsPerAppend + 1))
                .Select(i => AppendableEvent.Create<object>(new ChaosEvent { Operation = number, Index = i }))
        ];

        var commitId = Guid.NewGuid();
        var errors = new List<string>();

        while (true)
        {
            try
            {
                await eventStore.AppendAsync(
                    streamId,
                    events,
                    commitId: commitId,
                    options: AppendOptions,
                    cancellationToken: cancellationToken);

                return new AppendOperation(number, streamId, events.Length, AppendOutcome.Ok, errors);
            }
            // The writer doesn't expect a stream state, so no error proves that an append wasn't written.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                errors.Add(ex.GetType().Name);

                if (errors.Count == MaxAttempts)
                    return new AppendOperation(number, streamId, events.Length, AppendOutcome.Indeterminate, errors);

                if (retryCommitId == RetryCommitId.New)
                    commitId = Guid.NewGuid();

                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}