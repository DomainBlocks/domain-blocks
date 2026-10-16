using DomainBlocks.EventStore;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore;

/// <summary>
/// Reads a subscription one message at a time, for tests that interleave reading with appending events or releasing
/// held replays.
/// </summary>
public sealed class SubscriptionProbe<TStreamPos, TLogPos>(
    IAsyncEnumerable<SubscriptionMessage<object, string, TStreamPos, TLogPos>> subscription,
    CancellationToken cancellationToken) :
    IAsyncDisposable
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private readonly IAsyncEnumerator<SubscriptionMessage<object, string, TStreamPos, TLogPos>> _enumerator =
        subscription.GetAsyncEnumerator(cancellationToken);

    public async Task<SubscriptionMessage<object, string, TStreamPos, TLogPos>> NextAsync()
    {
        (await _enumerator.MoveNextAsync()).ShouldBeTrue();

        return _enumerator.Current;
    }

    /// <summary>
    /// Reads the next message, which must be an event, and returns its payload.
    /// </summary>
    public async Task<object> NextEventAsync()
    {
        var message = await NextAsync();
        message.Event.ShouldNotBeNull();

        return message.Event.Value.Payload;
    }

    /// <summary>
    /// Reads messages up to and including the next <c>CaughtUp</c>, starting with one that the test has already read,
    /// if given.
    /// </summary>
    public async Task<List<SubscriptionMessage<object, string, TStreamPos, TLogPos>>> ReadUntilCaughtUpAsync(
        SubscriptionMessage<object, string, TStreamPos, TLogPos>? alreadyRead = null)
    {
        List<SubscriptionMessage<object, string, TStreamPos, TLogPos>> messages = alreadyRead is { } message
            ? [message]
            : [];

        while (messages.Count == 0 || !messages[^1].IsCaughtUp)
            messages.Add(await NextAsync());

        return messages;
    }

    public ValueTask DisposeAsync() => _enumerator.DisposeAsync();
}