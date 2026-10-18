using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.Testing;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// Subscribes to a <see cref="TestEventLog"/> through its own
/// <see cref="CatchUpSubscriptionFactory{TSourceEvent,TResultEvent,TStreamId,TStreamPos,TLogPos}"/>, as a store does.
/// </summary>
internal sealed class SubscriptionScenario : IAsyncDisposable
{
    private static readonly EventLogTailOptions TailOptions = new()
    {
        // Long enough that only an append that is heard wakes the tail during a test.
        PollInterval = TimeSpan.FromHours(1)
    };

    private static readonly ILogger Logger = new NUnitLoggerProvider().CreateLogger(nameof(SubscriptionScenario));

    private readonly CatchUpSubscriptionFactory<TestSourceEvent, string, string, StreamPosition, LogPosition>
        _subscriptions;
    private readonly CancellationToken _cancellationToken;

    public SubscriptionScenario(CancellationToken cancellationToken)
    {
        _subscriptions = new CatchUpSubscriptionFactory<TestSourceEvent, string, string, StreamPosition, LogPosition>(
            EventLog.CreateSource,
            TailOptions,
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