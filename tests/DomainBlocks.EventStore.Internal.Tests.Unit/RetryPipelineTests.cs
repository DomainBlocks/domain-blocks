using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Polly;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit;

public class RetryPipelineTests
{
    private const int TestTimeoutMillis = 10_000;
    private const int MaxAttempts = 10;

    private static readonly TimeSpan RetryTimeout = TimeSpan.FromMinutes(1);

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ExecuteAsync_TransientErrors_RetriesUntilSuccess(CancellationToken ct)
    {
        var retries = new List<int>();
        var pipeline = CreatePipeline(RetryTimeout, new FakeTimeProvider(), retries);
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(ReadAsync, ct);

        result.ShouldBe(42);
        attempts.ShouldBe(3);
        retries.ShouldBe([1, 2]);

        ValueTask<int> ReadAsync(CancellationToken _) =>
            ++attempts < 3 ? throw new TransientException(attempts) : ValueTask.FromResult(42);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ExecuteAsync_OtherError_ThrowsWithoutRetry(CancellationToken ct)
    {
        var pipeline = CreatePipeline(RetryTimeout, new FakeTimeProvider(), []);
        var attempts = 0;

        await Should.ThrowAsync<InvalidOperationException>(pipeline.ExecuteAsync(ReadAsync, ct).AsTask());

        attempts.ShouldBe(1);

        ValueTask<int> ReadAsync(CancellationToken _)
        {
            attempts++;
            throw new InvalidOperationException();
        }
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ExecuteAsync_TimeoutBeforeMaxRetryAttempts_ThrowsLastError(CancellationToken ct)
    {
        var timeProvider = new FakeTimeProvider();
        var pipeline = CreatePipeline(RetryTimeout, timeProvider, [], maxRetryAttempts: 5);
        TimeSpan[] advances = [TimeSpan.Zero, RetryTimeout - TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)];
        var attempts = 0;

        var ex = await Should.ThrowAsync<TransientException>(pipeline.ExecuteAsync(ReadAsync, ct).AsTask());

        // The second failure came 1 tick before the timeout, and the third at it.
        ex.Attempt.ShouldBe(3);

        ValueTask<int> ReadAsync(CancellationToken _)
        {
            timeProvider.Advance(attempts < advances.Length ? advances[attempts] : TimeSpan.Zero);
            throw new TransientException(++attempts);
        }
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ExecuteAsync_MaxRetryAttemptsBeforeTimeout_ThrowsLastError(CancellationToken ct)
    {
        var retries = new List<int>();
        var pipeline = CreatePipeline(RetryTimeout, new FakeTimeProvider(), retries, maxRetryAttempts: 2);
        var attempts = 0;

        var ex = await Should.ThrowAsync<TransientException>(pipeline.ExecuteAsync(ReadAsync, ct).AsTask());

        // The clock never moves, so the timeout isn't reached.
        ex.Attempt.ShouldBe(3);
        retries.ShouldBe([1, 2]);

        ValueTask<int> ReadAsync(CancellationToken _) =>
            ++attempts == MaxAttempts ? ValueTask.FromResult(0) : throw new TransientException(attempts);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ExecuteAsync_ZeroMaxRetryAttempts_ThrowsWithoutRetry(CancellationToken ct)
    {
        var retries = new List<int>();
        var pipeline = CreatePipeline(RetryTimeout, new FakeTimeProvider(), retries, maxRetryAttempts: 0);
        var attempts = 0;

        await Should.ThrowAsync<TransientException>(pipeline.ExecuteAsync(ReadAsync, ct).AsTask());

        attempts.ShouldBe(1);
        retries.ShouldBeEmpty();

        ValueTask<int> ReadAsync(CancellationToken _) =>
            ++attempts == MaxAttempts ? ValueTask.FromResult(0) : throw new TransientException(attempts);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ExecuteAsync_InfiniteTimeout_KeepsRetrying(CancellationToken ct)
    {
        var timeProvider = new FakeTimeProvider();
        var pipeline = CreatePipeline(Timeout.InfiniteTimeSpan, timeProvider, []);
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(ReadAsync, ct);

        result.ShouldBe(42);
        attempts.ShouldBe(4);

        ValueTask<int> ReadAsync(CancellationToken _)
        {
            if (++attempts == 4)
                return ValueTask.FromResult(42);

            timeProvider.Advance(TimeSpan.FromDays(3650));
            throw new TransientException(attempts);
        }
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ExecuteAsync_NewExecutionAfterTimeout_StartsNewTimeout(CancellationToken ct)
    {
        var timeProvider = new FakeTimeProvider();
        var pipeline = CreatePipeline(RetryTimeout, timeProvider, []);
        var step = 0;

        await pipeline.ExecuteAsync(ReadAsync, ct);
        await pipeline.ExecuteAsync(ReadAsync, ct);

        step.ShouldBe(5);

        // The first execution's failure is more than the timeout before the second's, which still retries.
        ValueTask<int> ReadAsync(CancellationToken _)
        {
            switch (++step)
            {
                case 2:
                    timeProvider.Advance(2 * RetryTimeout);
                    return ValueTask.FromResult(0);
                case 4:
                    timeProvider.Advance(RetryTimeout - TimeSpan.FromTicks(1));
                    throw new TransientException(step);
                case 1 or 3:
                    throw new TransientException(step);
                default:
                    return ValueTask.FromResult(0);
            }
        }
    }

    [Test]
    public async Task ExecuteAsync_CallerCanceled_ThrowsWithoutRetry()
    {
        var pipeline = CreatePipeline(RetryTimeout, new FakeTimeProvider(), []);
        using var cancellationTokenSource = new CancellationTokenSource();
        var attempts = 0;

        await Should.ThrowAsync<TransientException>(
            pipeline.ExecuteAsync(ReadAsync, cancellationTokenSource.Token).AsTask());

        attempts.ShouldBe(1);

        ValueTask<int> ReadAsync(CancellationToken _)
        {
            if (++attempts == MaxAttempts)
                return ValueTask.FromResult(0);

            // ReSharper disable once AccessToDisposedClosure - only called during the execution
            cancellationTokenSource.Cancel();
            throw new TransientException(attempts);
        }
    }

    // Retries at once, and records each retry's number.
    private static ResiliencePipeline CreatePipeline(
        TimeSpan timeout,
        TimeProvider timeProvider,
        List<int> retries,
        int maxRetryAttempts = int.MaxValue)
    {
        return RetryPipeline.Create(
            static ex => ex is TransientException,
            maxRetryAttempts,
            timeout,
            (_, attempt, _) => retries.Add(attempt),
            delay: TimeSpan.Zero,
            maxDelay: TimeSpan.Zero,
            timeProvider: timeProvider);
    }

    private sealed class TransientException(int attempt) : Exception($"Attempt {attempt} failed.")
    {
        public int Attempt { get; } = attempt;
    }
}