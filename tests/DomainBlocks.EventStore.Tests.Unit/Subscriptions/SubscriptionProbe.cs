using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// Reads a subscription one message at a time, for tests that interleave reading with appending events or releasing
/// held catch-ups.
/// </summary>
internal sealed class SubscriptionProbe(
    IAsyncEnumerable<SubscriptionMessage<string, string, StreamPosition, LogPosition>> subscription,
    CancellationToken cancellationToken) :
    IAsyncDisposable
{
    private readonly IAsyncEnumerator<SubscriptionMessage<string, string, StreamPosition, LogPosition>> _enumerator =
        subscription.GetAsyncEnumerator(cancellationToken);

    public async Task<SubscriptionMessage<string, string, StreamPosition, LogPosition>> NextAsync()
    {
        (await _enumerator.MoveNextAsync()).ShouldBeTrue();

        return _enumerator.Current;
    }

    /// <summary>
    /// Reads the next message, which must be an event, and returns its payload.
    /// </summary>
    public async Task<string> NextEventAsync()
    {
        var message = await NextAsync();
        message.Event.ShouldNotBeNull();

        return message.Event.Value.Payload;
    }

    /// <summary>
    /// Reads messages up to and including the next <c>CaughtUp</c>, starting with one that the test has already read,
    /// if given.
    /// </summary>
    public async Task<List<SubscriptionMessage<string, string, StreamPosition, LogPosition>>> ReadUntilCaughtUpAsync(
        SubscriptionMessage<string, string, StreamPosition, LogPosition>? alreadyRead = null)
    {
        List<SubscriptionMessage<string, string, StreamPosition, LogPosition>> messages = alreadyRead is { } message
            ? [message]
            : [];

        while (messages.Count == 0 || !messages[^1].IsCaughtUp)
            messages.Add(await NextAsync());

        return messages;
    }

    public ValueTask DisposeAsync() => _enumerator.DisposeAsync();
}