namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// Counts the payloads that are decoded, and fails to decode them while <see cref="Failure"/> is set.
/// </summary>
internal sealed class TestDecoder
{
    private int _decodeCount;

    public int DecodeCount => Volatile.Read(ref _decodeCount);

    public Exception? Failure { get; set; }

    public string Decode(string payload)
    {
        Interlocked.Increment(ref _decodeCount);

        return Failure is { } failure ? throw failure : payload;
    }
}