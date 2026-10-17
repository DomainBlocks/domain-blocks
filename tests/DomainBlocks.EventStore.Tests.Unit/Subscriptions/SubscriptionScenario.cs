using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.Testing;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// Subscribes to a <see cref="TestEventLog"/> through its own
/// <see cref="EventLogSubscriptions{TEvent,TSourceEvent,TStreamId,TStreamPos,TLogPos}"/>, as a store does.
/// </summary>
internal sealed class SubscriptionScenario : IAsyncDisposable
{
    private static readonly ILogger Logger = new NUnitLoggerProvider().CreateLogger(nameof(SubscriptionScenario));

    private readonly EventLogSubscriptions<string, TestSourceEvent, string, StreamPosition, LogPosition> _subscriptions;
    private readonly CancellationToken _cancellationToken;

    /// <param name="cancellationToken">The test's token, which every read of a subscription takes.</param>
    /// <param name="retryTimeout">How long reads that keep failing are retried, by default the store's default.</param>
    public SubscriptionScenario(CancellationToken cancellationToken, TimeSpan? retryTimeout = null)
    {
        var tailOptions = new EventLogTailOptions
        {
            // Long enough that only an append that is heard wakes the tail during a test.
            PollInterval = TimeSpan.FromHours(1),

            // Short, so that a test that retries a read does not wait for long.
            RetryDelay = TimeSpan.FromMilliseconds(10),
            MaxRetryDelay = TimeSpan.FromMilliseconds(50),
            RetryTimeout = retryTimeout ?? EventLogTailOptions.Default.RetryTimeout
        };

        _subscriptions = new EventLogSubscriptions<string, TestSourceEvent, string, StreamPosition, LogPosition>(
            EventLog.CreateSource,
            tailOptions,
            Logger);

        _cancellationToken = cancellationToken;
    }

    public TestEventLog EventLog { get; } = new();

    public SubscriptionProbe SubscribeToAll(
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        return new SubscriptionProbe(_subscriptions.SubscribeToAll(origin, options), _cancellationToken);
    }

    public SubscriptionProbe SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        return new SubscriptionProbe(_subscriptions.SubscribeToStream(streamId, origin, options), _cancellationToken);
    }

    public ValueTask DisposeAsync() => _subscriptions.DisposeAsync();
}