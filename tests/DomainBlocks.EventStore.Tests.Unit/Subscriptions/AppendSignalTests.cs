using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.Testing;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

public class AppendSignalTests
{
    private const int TestTimeoutMillis = 5 * 1000;
    private static readonly TimeSpan LongTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly EventLogTailOptions Options = new()
    {
        RetryDelay = TimeSpan.FromMilliseconds(10),
        MaxRetryDelay = TimeSpan.FromMilliseconds(50)
    };

    private static readonly ILogger Logger = new NUnitLoggerProvider().CreateLogger(nameof(AppendSignalTests));

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitAsync_LaterAppendAlreadyHeard_ReturnsTrueAtOnce(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        await using var signal = await StartAsync(listener, ct);
        await listener.HearAsync(5, ct);

        (await signal.WaitAsync(4, LongTimeout, ct)).ShouldBeTrue();
        (await signal.WaitAsync(null, LongTimeout, ct)).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitAsync_NothingHeard_ReturnsFalseAfterTimeout(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        await using var signal = await StartAsync(listener, ct);

        (await signal.WaitAsync(null, ShortTimeout, ct)).ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitAsync_AppendHeardOnlyAtPosition_ReturnsFalseAfterTimeout(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        await using var signal = await StartAsync(listener, ct);
        await listener.HearAsync(5, ct);

        (await signal.WaitAsync(5, ShortTimeout, ct)).ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitAsync_SeveralWaiters_EachReturnsWhenAnAppendAfterItsPositionIsHeard(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        await using var signal = await StartAsync(listener, ct);
        var afterOne = signal.WaitAsync(1, LongTimeout, ct).AsTask();
        var afterThree = signal.WaitAsync(3, LongTimeout, ct).AsTask();

        await listener.HearAsync(2, ct);

        (await afterOne).ShouldBeTrue();
        await Task.Delay(100, ct);
        afterThree.IsCompleted.ShouldBeFalse();

        await listener.HearAsync(4, ct);

        (await afterThree).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitAsync_Canceled_Throws(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        await using var signal = await StartAsync(listener, ct);
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(50);

        var wait = signal.WaitAsync(null, LongTimeout, waitCts.Token).AsTask();

        await Should.ThrowAsync<OperationCanceledException>(wait);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task HighWaterMark_EarlierAppendHeard_KeepsTheLaterOne(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        await using var signal = await StartAsync(listener, ct);
        await listener.HearAsync(5, ct);

        await listener.HearAsync(3, ct);

        signal.HighWaterMark.ShouldBe(5);
        (await signal.WaitAsync(4, ShortTimeout, ct)).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Start_ListenerFailsBeforeHearing_ListensAgain(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        await using var signal = await StartAsync(listener, ct);

        listener.Fail(new IOException("lost"));
        await listener.WaitForStartAsync(ct);
        await listener.HearAsync(1, ct);

        listener.StartCount.ShouldBe(2);
        signal.HighWaterMark.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Start_ListenerFailsAfterHearing_ListensAgainWithoutBackoff(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        var options = new EventLogTailOptions { RetryDelay = LongTimeout, MaxRetryDelay = LongTimeout };
        await using var signal = await StartAsync(listener, ct, options);
        await listener.HearAsync(1, ct);

        listener.Fail(new IOException("lost"));
        await listener.WaitForStartAsync(ct);

        await listener.HearAsync(2, ct);

        signal.HighWaterMark.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Start_ListenerEnds_ListensAgain(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        await using var signal = await StartAsync(listener, ct);

        listener.End();
        await listener.WaitForStartAsync(ct);

        listener.StartCount.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_Listening_StopsTheListener(CancellationToken ct)
    {
        var listener = new TestAppendListener();
        var signal = await StartAsync(listener, ct);

        await signal.DisposeAsync();

        listener.ActiveCount.ShouldBe(0);
    }

    private static async Task<AppendSignal<long>> StartAsync(
        TestAppendListener listener,
        CancellationToken cancellationToken,
        EventLogTailOptions? options = null)
    {
        var signal = new AppendSignal<long>(listener.ListenAsync, options ?? Options, Logger, "test");
        signal.Start();
        await listener.WaitForStartAsync(cancellationToken);

        return signal;
    }

    /// <summary>
    /// A store's listener, which yields the positions that the test hears, and fails or ends when the test says.
    /// </summary>
    private sealed class TestAppendListener
    {
        private readonly SemaphoreSlim _starts = new(0);
        private readonly SemaphoreSlim _consumptions = new(0);
        private Channel<long> _current = Channel.CreateUnbounded<long>();
        private int _startCount;
        private int _activeCount;

        public int StartCount => Volatile.Read(ref _startCount);

        public int ActiveCount => Volatile.Read(ref _activeCount);

        public async IAsyncEnumerable<long> ListenAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var channel = Channel.CreateUnbounded<long>();
            Volatile.Write(ref _current, channel);
            Interlocked.Increment(ref _startCount);
            Interlocked.Increment(ref _activeCount);
            _starts.Release();

            try
            {
                await foreach (var position in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return position;

                    // The signal asks for the next position only once it has taken this one in.
                    _consumptions.Release();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
            }
        }

        /// <summary>
        /// Hears an append, and waits until the signal has taken it in.
        /// </summary>
        public async Task HearAsync(long position, CancellationToken cancellationToken)
        {
            Volatile.Read(ref _current).Writer.TryWrite(position);
            await _consumptions.WaitAsync(cancellationToken);
        }

        public void Fail(Exception exception) => Volatile.Read(ref _current).Writer.TryComplete(exception);

        public void End() => Volatile.Read(ref _current).Writer.TryComplete();

        public Task WaitForStartAsync(CancellationToken cancellationToken) => _starts.WaitAsync(cancellationToken);
    }
}