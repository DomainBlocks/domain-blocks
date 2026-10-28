using System.Runtime.CompilerServices;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit;

public class CatchUpRetryTests
{
    private const int TestTimeoutMillis = 10_000;

    private static readonly TimeSpan ShortDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_TransientErrors_RetriesUntilSuccess(CancellationToken ct)
    {
        var retries = new List<Exception>();
        var retry = CreateRetry(ShortTimeout, ShortDelay, retries);
        var attempts = 0;

        var result = await retry.ReadAsync(ReadAsync, ct);

        result.ShouldBe(42);
        attempts.ShouldBe(3);
        retries.Count.ShouldBe(2);

        Task<int> ReadAsync(CancellationToken _) =>
            ++attempts < 3 ? Task.FromException<int>(new TransientException(attempts)) : Task.FromResult(42);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_SuccessAfterRetries_RestartsFromFirstDelay(CancellationToken ct)
    {
        var firstDelay = TimeSpan.FromMilliseconds(10);
        var delays = new List<TimeSpan>();

        var retry = new CatchUpRetry(
            static ex => ex is TransientException,
            new RetryBackoff(ShortTimeout * 10, firstDelay, TimeSpan.FromSeconds(1), TimeProvider.System),
            (_, delay) => delays.Add(delay));

        var failures = 3;
        await retry.ReadAsync(ReadAsync, ct);
        failures = 1;
        await retry.ReadAsync(ReadAsync, ct);

        delays.Count.ShouldBe(4);
        delays[^1].ShouldBeLessThanOrEqualTo(firstDelay);

        Task<int> ReadAsync(CancellationToken _) =>
            failures-- > 0 ? Task.FromException<int>(new TransientException(failures)) : Task.FromResult(0);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_OtherError_ThrowsWithoutRetry(CancellationToken ct)
    {
        var retry = CreateRetry(ShortTimeout, ShortDelay, []);
        var attempts = 0;

        await Should.ThrowAsync<InvalidOperationException>(retry.ReadAsync(ReadAsync, ct));

        attempts.ShouldBe(1);

        Task<int> ReadAsync(CancellationToken _)
        {
            attempts++;
            return Task.FromException<int>(new InvalidOperationException());
        }
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_TransientErrorsPastTimeout_ThrowsLastError(CancellationToken ct)
    {
        var retry = CreateRetry(ShortTimeout, ShortDelay, []);
        var attempts = 0;

        var ex = await Should.ThrowAsync<TransientException>(retry.ReadAsync(ReadAsync, ct));

        // The retries stopped at the retry timeout, not at the test's.
        ct.IsCancellationRequested.ShouldBeFalse();
        attempts.ShouldBeGreaterThan(1);
        ex.Attempt.ShouldBe(attempts);

        Task<int> ReadAsync(CancellationToken _) => Task.FromException<int>(new TransientException(++attempts));
    }

    [Test]
    public async Task ReadAsync_CallerCanceled_ThrowsWithoutRetry()
    {
        var retry = CreateRetry(ShortTimeout, ShortDelay, []);
        using var cancellationTokenSource = new CancellationTokenSource();
        var attempts = 0;

        await Should.ThrowAsync<TransientException>(retry.ReadAsync(ReadAsync, cancellationTokenSource.Token));

        attempts.ShouldBe(1);

        Task<int> ReadAsync(CancellationToken _)
        {
            cancellationTokenSource.Cancel();
            return Task.FromException<int>(new TransientException(++attempts));
        }
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReplayAsync_TransientErrorMidReplay_ResumesAfterLastTakenItem(CancellationToken ct)
    {
        var sequence = new Sequence(5, (open, item) => open == 0 && item == 2 ? new TransientException(1) : null);
        var retry = CreateRetry(ShortTimeout, ShortDelay, []);

        var items = await ReplayAsync(retry, sequence, ct).ToListAsync();

        items.ShouldBe([0, 1, 2, 3, 4]);
        sequence.Starts.ShouldBe([-1, 1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReplayAsync_OtherErrorMidReplay_ThrowsAfterTakenItems(CancellationToken ct)
    {
        var sequence = new Sequence(5, (_, item) => item == 2 ? new InvalidOperationException() : null);
        var retry = CreateRetry(ShortTimeout, ShortDelay, []);
        var items = new List<int>();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in ReplayAsync(retry, sequence, ct))
                items.Add(item);
        });

        items.ShouldBe([0, 1]);
        sequence.Starts.ShouldBe([-1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReplayAsync_ErrorAfterEachItemPastTimeout_DeliversAllItems(CancellationToken ct)
    {
        // Each open fails after one item, so the replay fails for longer than the timeout in all, but never for longer
        // than the timeout without progress.
        var sequence = new Sequence(20, (open, item) => item == open + 1 ? new TransientException(open) : null);
        var retry = CreateRetry(ShortTimeout, TimeSpan.FromMilliseconds(10), []);

        var items = await ReplayAsync(retry, sequence, ct).ToListAsync();

        items.ShouldBe(Enumerable.Range(0, 20));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReplayAsync_TransientErrorsWithoutProgress_ThrowsLastError(CancellationToken ct)
    {
        var sequence = new Sequence(5, (open, _) => new TransientException(open));
        var retry = CreateRetry(ShortTimeout, ShortDelay, []);

        var ex = await Should.ThrowAsync<TransientException>(async () =>
            await ReplayAsync(retry, sequence, ct).ToListAsync());

        // The retries stopped at the retry timeout, not at the test's.
        ct.IsCancellationRequested.ShouldBeFalse();
        sequence.Starts.Count.ShouldBeGreaterThan(1);
        ex.Attempt.ShouldBe(sequence.Starts.Count - 1);
    }

    private static CatchUpRetry CreateRetry(TimeSpan timeout, TimeSpan delay, List<Exception> retries)
    {
        return new CatchUpRetry(
            static ex => ex is TransientException,
            new RetryBackoff(timeout, delay, delay, TimeProvider.System),
            (ex, _) => retries.Add(ex));
    }

    // Replays the sequence from its start, opening it again after the last item taken.
    private static IAsyncEnumerable<int> ReplayAsync(CatchUpRetry retry, Sequence sequence, CancellationToken ct)
    {
        var lastTaken = -1;

        return retry.ReplayAsync(
            ct => sequence.OpenAsync(lastTaken, ct),
            item => lastTaken = item,
            ct);
    }

    private sealed class TransientException(int attempt) : Exception($"Attempt {attempt} failed.")
    {
        public int Attempt { get; } = attempt;
    }

    // The integers from 0 to count - 1. It records where each open starts, and before each item it throws the error
    // that errorBefore returns for the open's index and the item, if any.
    private sealed class Sequence(int count, Func<int, int, Exception?> errorBefore)
    {
        private readonly List<int> _starts = [];

        public IReadOnlyList<int> Starts => _starts;

        public async IAsyncEnumerable<int> OpenAsync(
            int startExclusive,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var open = _starts.Count;
            _starts.Add(startExclusive);

            for (var item = startExclusive + 1; item < count; item++)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                if (errorBefore(open, item) is { } error)
                    throw error;

                yield return item;
            }
        }
    }
}