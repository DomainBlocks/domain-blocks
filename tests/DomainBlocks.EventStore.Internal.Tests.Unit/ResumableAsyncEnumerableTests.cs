using System.Runtime.CompilerServices;
using NUnit.Framework;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit;

public class ResumableAsyncEnumerableTests
{
    private const int TestTimeoutMillis = 10_000;

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_TransientErrorMidSequence_ResumesAfterLastTakenItem(CancellationToken ct)
    {
        var sequence = new Sequence(5, (instance, item) =>
            instance == 0 && item == 2 ? new TransientException() : null);

        var items = await Resume(sequence, CreatePipeline()).ToListAsync(ct);

        items.ShouldBe([0, 1, 2, 3, 4]);
        sequence.Starts.ShouldBe([-1, 1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_TransientErrorBeforeFirstItem_CreatesAgain(CancellationToken ct)
    {
        var sequence = new Sequence(3, (instance, item) =>
            instance == 0 && item == 0 ? new TransientException() : null);

        var items = await Resume(sequence, CreatePipeline()).ToListAsync(ct);

        items.ShouldBe([0, 1, 2]);
        sequence.Starts.ShouldBe([-1, -1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_FactoryThrowsTransientError_CreatesAgain(CancellationToken ct)
    {
        var sequence = new Sequence(3, static (_, _) => null);
        var creations = 0;

        var resumable = ResumableAsyncEnumerable.Create(
            (s, after, token) => ++creations == 1 ? throw new TransientException() : s.CreateAsync(after, token),
            static (_, item) => item,
            sequence,
            -1,
            CreatePipeline());

        var items = await resumable.ToListAsync(ct);

        items.ShouldBe([0, 1, 2]);
        creations.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_RetriesFailBeforeFirstItem_StopsAtPipelineLimit(CancellationToken ct)
    {
        var sequence = new Sequence(5, (instance, item) => instance > 0 || item == 2 ? new TransientException() : null);
        var retries = new List<int>();
        var items = new List<int>();

        await Should.ThrowAsync<TransientException>(async () =>
        {
            await foreach (var item in Resume(sequence, CreatePipeline(3, retries)).WithCancellation(ct))
                items.Add(item);
        });

        // The retries made no progress, so they were all attempts of one recovery.
        items.ShouldBe([0, 1]);
        sequence.Starts.ShouldBe([-1, 1, 1, 1]);
        retries.ShouldBe([0, 1, 2]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_ProgressBetweenErrors_StartsNewRecovery(CancellationToken ct)
    {
        // Each instance fails after its first item, so the ten items take nine recoveries, each allowed one retry.
        var sequence = new Sequence(10, (instance, item) => item == instance + 1 ? new TransientException() : null);
        var retries = new List<int>();

        var items = await Resume(sequence, CreatePipeline(1, retries)).ToListAsync(ct);

        items.ShouldBe(Enumerable.Range(0, 10));
        retries.ShouldBe(Enumerable.Repeat(0, 9));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_OtherError_ThrowsSameErrorWithoutRetrying(CancellationToken ct)
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
    public async Task GetAsyncEnumerator_CallerCanceled_ThrowsErrorWithoutRetrying()
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
    public async Task GetAsyncEnumerator_ErrorMidSequence_DisposesFailedSequenceBeforeCreatingAnother(
        CancellationToken ct)
    {
        var sequence = new Sequence(3, (instance, item) =>
            instance == 0 && item == 1 ? new TransientException() : null);

        await Resume(sequence, CreatePipeline()).ToListAsync(ct);

        sequence.Events.ShouldBe(["create 0", "dispose 0", "create 1", "dispose 1"]);
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

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_FirstReadOutlastsAttemptTimeout_CreatesAgain(CancellationToken ct)
    {
        var sequence = new Sequence(3, static (_, _) => null, static (instance, item) => instance == 0 && item == 0);

        var items = await Resume(sequence, CreateTimedPipeline(TimeSpan.FromMilliseconds(50))).ToListAsync(ct);

        items.ShouldBe([0, 1, 2]);
        sequence.Starts.ShouldBe([-1, -1]);
    }

    [Test]
    public async Task GetAsyncEnumerator_CallerCancelsAfterTimedAttempt_CancelsSequence()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var sequence = new Sequence(3, static (_, _) => null, static (_, item) => item == 1);
        var resumable = Resume(sequence, CreateTimedPipeline(TimeSpan.FromMinutes(1)));
        var items = resumable.GetAsyncEnumerator(cancellationTokenSource.Token);

        await using (items)
        {
            (await items.MoveNextAsync()).ShouldBeTrue();
            var next = items.MoveNextAsync().AsTask();

            await cancellationTokenSource.CancelAsync();

            // A sequence that kept the attempt's token wouldn't see the cancel, so the wait would time out.
            await Should.ThrowAsync<OperationCanceledException>(
                next.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        }
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

    // Gives each attempt the timeout, and retries an attempt that times out at once.
    private static ResiliencePipeline CreateTimedPipeline(TimeSpan attemptTimeout)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = static args => new ValueTask<bool>(args.Outcome.Exception is TimeoutRejectedException),
                Delay = TimeSpan.Zero
            })
            .AddTimeout(attemptTimeout)
            .Build();
    }

    // Reads the sequence from its start, creating it again after the last item taken. Each item is its own position.
    private static ResumableAsyncEnumerable<int, Sequence, int> Resume(Sequence sequence, ResiliencePipeline pipeline)
    {
        return ResumableAsyncEnumerable.Create(
            static (s, after, ct) => s.CreateAsync(after, ct),
            static (_, item) => item,
            sequence,
            -1,
            pipeline);
    }

    private sealed class TransientException : Exception;

    // The integers from 0 to count - 1. It records where each instance it creates starts, and when each is created and
    // the caller disposes it. Before each item it throws the error that errorBefore returns for the instance's index
    // and the item, if any, and it waits until it's canceled if waitBefore returns true for them.
    private sealed class Sequence(
        int count,
        Func<int, int, Exception?> errorBefore,
        Func<int, int, bool>? waitBefore = null)
    {
        public List<int> Starts { get; } = [];

        public List<string> Events { get; } = [];

        public IAsyncEnumerable<int> CreateAsync(int startExclusive, CancellationToken cancellationToken)
        {
            var instance = Starts.Count;
            Starts.Add(startExclusive);
            Events.Add($"create {instance}");

            var items = ReadAsync(instance, startExclusive, cancellationToken);

            return new DisposeRecorder(items, () => Events.Add($"dispose {instance}"));
        }

        private async IAsyncEnumerable<int> ReadAsync(
            int instance,
            int startExclusive,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var item = startExclusive + 1; item < count; item++)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                if (errorBefore(instance, item) is { } error)
                    throw error;

                if (waitBefore?.Invoke(instance, item) == true)
                    await Task.Delay(Timeout.Infinite, cancellationToken);

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