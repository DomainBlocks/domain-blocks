using DomainBlocks.Testing.Events;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Holds a lone append's batch inside the database, on the sequence row. Appends made meanwhile queue up behind it, so
/// they go out together in the next batch once the hold is released.
/// </summary>
internal sealed class HeldBatch(SequenceRowLock rowLock, Task append) : IAsyncDisposable
{
    public const string StreamId = "held";

    public static async Task<HeldBatch> StartAsync(
        IEventStore<object, string, StreamPosition, LogPosition> eventStore,
        NpgsqlDataSource dataSource,
        string schema,
        CancellationToken ct)
    {
        var rowLock = await SequenceRowLock.AcquireAsync(dataSource, schema, ct);

        var append = eventStore.AppendAsync(
            StreamId,
            [AppendableEvent.Create<object>(new TestEvent { Value = StreamId })],
            cancellationToken: ct);

        await rowLock.WaitUntilBlockingAsync(ct);
        return new HeldBatch(rowLock, append);
    }

    // Returns once the held batch has committed.
    public async Task ReleaseAsync(CancellationToken ct)
    {
        await rowLock.ReleaseAsync(ct);
        await append;
    }

    public ValueTask DisposeAsync() => rowLock.DisposeAsync();
}