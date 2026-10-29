using System.Runtime.CompilerServices;
using NUnit.Framework;
using Polly;
using Polly.Retry;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit;

public class ResumableAsyncEnumerableTests
{
    private const int TestTimeoutMillis = 10_000;

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_TransientErrorMidSequence_ResumesAfterLastTakenItem(CancellationToken ct)
    {
        var sequence = new Sequence(5, (open, item) => open == 0 && item == 2 ? new TransientException() : null);

        var items = await Resume(sequence, CreatePipeline()).ToListAsync(ct);

        items.ShouldBe([0, 1, 2, 3, 4]);
        sequence.Starts.ShouldBe([-1, 1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_TransientErrorBeforeFirstItem_OpensAgain(CancellationToken ct)
    {
        var sequence = new Sequence(3, (open, item) => open == 0 && item == 0 ? new TransientException() : null);

        var items = await Resume(sequence, CreatePipeline()).ToListAsync(ct);

        items.ShouldBe([0, 1, 2]);
        sequence.Starts.ShouldBe([-1, -1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_OpenThrowsTransientError_OpensAgain(CancellationToken ct)
    {
        var sequence = new Sequence(3, static (_, _) => null);
        var opens = 0;

        var resumable = ResumableAsyncEnumerable.Create(
            sequence,
            -1,
            (s, after, token) => ++opens == 1 ? throw new TransientException() : s.OpenAsync(after, token),
            static (_, item) => item,
            CreatePipeline());

        var items = await resumable.ToListAsync(ct);

        items.ShouldBe([0, 1, 2]);
        opens.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_ReopensFailBeforeFirstItem_StopsAtPipelineLimit(CancellationToken ct)
    {
        var sequence = new Sequence(5, (open, item) => open > 0 || item == 2 ? new TransientException() : null);
        var retries = new List<int>();
        var items = new List<int>();

        await Should.ThrowAsync<TransientException>(async () =>
        {
            await foreach (var item in Resume(sequence, CreatePipeline(3, retries)).WithCancellation(ct))
                items.Add(item);
        });

        // The reopens made no progress, so they were all attempts of one recovery.
        items.ShouldBe([0, 1]);
        sequence.Starts.ShouldBe([-1, 1, 1, 1]);
        retries.ShouldBe([0, 1, 2]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_ProgressBetweenErrors_StartsNewRecovery(CancellationToken ct)
    {
        // Each open fails after one item, so the sequence needs nine recoveries, each within a limit of one retry.
        var sequence = new Sequence(10, (open, item) => item == open + 1 ? new TransientException() : null);
        var retries = new List<int>();

        var items = await Resume(sequence, CreatePipeline(1, retries)).ToListAsync(ct);

        items.ShouldBe(Enumerable.Range(0, 10));
        retries.ShouldBe(Enumerable.Repeat(0, 9));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_OtherError_ThrowsSameErrorWithoutReopening(CancellationToken ct)
    {
        var error = new InvalidOperationException();
        var sequence = new Sequence(5, (_, item) => item == 2 ? error : null);
        var retries = new List<int>();
        var items = new List<int>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in Resume(sequence, CreatePipeline(3, retries)).WithCancellation(ct))
                items.Add(item);
        });

        ex.ShouldBeSameAs(error);
        items.ShouldBe([0, 1]);
        sequence.Starts.ShouldBe([-1]);
        retries.ShouldBeEmpty();
    }

    [Test]
    public async Task GetAsyncEnumerator_CallerCanceled_ThrowsErrorWithoutReopening()
    {
        using var cancellationTokenSource = new CancellationTokenSource();

        var sequence = new Sequence(5, (_, item) =>
        {
            if (item != 2)
                return null;

            cancellationTokenSource.Cancel();
            return new TransientException();
        });

        await Should.ThrowAsync<TransientException>(async () =>
            await Resume(sequence, CreatePipeline()).ToListAsync(cancellationTokenSource.Token));

        sequence.Starts.ShouldBe([-1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_ErrorMidSequence_DisposesFailedSequenceBeforeOpeningAgain(CancellationToken ct)
    {
        var sequence = new Sequence(3, (open, item) => open == 0 && item == 1 ? new TransientException() : null);

        await Resume(sequence, CreatePipeline()).ToListAsync(ct);

        sequence.Events.ShouldBe(["open 0", "dispose 0", "open 1", "dispose 1"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_SecondEnumeration_StartsAtInitialPosition(CancellationToken ct)
    {
        var sequence = new Sequence(3, static (_, _) => null);
        var resumable = Resume(sequence, CreatePipeline());

        (await resumable.ToListAsync(ct)).ShouldBe([0, 1, 2]);
        (await resumable.ToListAsync(ct)).ShouldBe([0, 1, 2]);

        sequence.Starts.ShouldBe([-1, -1]);
    }

    // Retries transient errors at once, and records the number of each retry's attempt, from 0.
    private static ResiliencePipeline CreatePipeline(int maxRetryAttempts = 3, List<int>? retries = null)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = static args => new ValueTask<bool>(args.Outcome.Exception is TransientException),
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.Zero,
                OnRetry = args =>
                {
                    retries?.Add(args.AttemptNumber);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    // Reads the sequence from its start, opening it again after the last item taken. Each item is its own position.
    private static ResumableAsyncEnumerable<int, Sequence, int> Resume(Sequence sequence, ResiliencePipeline pipeline)
    {
        return ResumableAsyncEnumerable.Create(
            sequence,
            -1,
            static (s, after, ct) => s.OpenAsync(after, ct),
            static (_, item) => item,
            pipeline);
    }

    private sealed class TransientException : Exception;

    // The integers from 0 to count - 1. It records where each open starts, and when each open starts and the caller
    // disposes it. Before each item it throws the error that errorBefore returns for the open's index and the item, if
    // any.
    private sealed class Sequence(int count, Func<int, int, Exception?> errorBefore)
    {
        public List<int> Starts { get; } = [];

        public List<string> Events { get; } = [];

        public IAsyncEnumerable<int> OpenAsync(int startExclusive, CancellationToken cancellationToken)
        {
            var open = Starts.Count;
            Starts.Add(startExclusive);
            Events.Add($"open {open}");

            var items = ReadAsync(open, startExclusive, cancellationToken);

            return new DisposeRecorder(items, () => Events.Add($"dispose {open}"));
        }

        private async IAsyncEnumerable<int> ReadAsync(
            int open,
            int startExclusive,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
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

    // Records the caller's DisposeAsync, which an iterator's finally block can't tell from an error leaving it.
    private sealed class DisposeRecorder(IAsyncEnumerable<int> items, Action onDispose) : IAsyncEnumerable<int>
    {
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(items.GetAsyncEnumerator(cancellationToken), onDispose);

        private sealed class Enumerator(IAsyncEnumerator<int> items, Action onDispose) : IAsyncEnumerator<int>
        {
            public int Current => items.Current;

            public ValueTask<bool> MoveNextAsync() => items.MoveNextAsync();

            public ValueTask DisposeAsync()
            {
                onDispose();
                return items.DisposeAsync();
            }
        }
    }
}