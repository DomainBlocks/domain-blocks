using DomainBlocks.EventStore;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore;

/// <summary>
/// Reads a subscription one message at a time, for tests that interleave reading with appending events or releasing
/// held replays.
/// </summary>
/// <remarks>
/// Disposing the probe cancels a read that is still pending, and waits for it to end, before it disposes the
/// subscription. Disposing a subscription during a read throws, and that exception would hide the failure that ended
/// the test.
/// </remarks>
public sealed class SubscriptionProbe<TStreamPos, TLogPos> : IAsyncDisposable
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private readonly CancellationTokenSource _cancellation;
    private readonly IAsyncEnumerator<SubscriptionMessage<object, string, TStreamPos, TLogPos>> _enumerator;
    private Task _read = Task.CompletedTask;

    public SubscriptionProbe(
        IAsyncEnumerable<SubscriptionMessage<object, string, TStreamPos, TLogPos>> subscription,
        CancellationToken cancellationToken)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _enumerator = subscription.GetAsyncEnumerator(_cancellation.Token);
    }

    public Task<SubscriptionMessage<object, string, TStreamPos, TLogPos>> NextAsync()
    {
        var read = ReadNextAsync();
        _read = read;

        return read;
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

    public async ValueTask DisposeAsync()
    {
        if (!_read.IsCompleted)
        {
            await _cancellation.CancelAsync();
            await _read.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await _enumerator.DisposeAsync();
        _cancellation.Dispose();
    }

    private async Task<SubscriptionMessage<object, string, TStreamPos, TLogPos>> ReadNextAsync()
    {
        (await _enumerator.MoveNextAsync()).ShouldBeTrue();

        return _enumerator.Current;
    }
}