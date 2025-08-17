using DomainBlocks.Persistence.Abstractions.Events;
using KurrentDB.Client;

// DomainBlocks.Serialization.MongoDB.Bson

namespace DomainBlocks.Persistence.KurrentDB.Events;

public class KurrentDBEventDataStore(KurrentDBClient client) : IEventDataStore<ReadOnlyMemory<byte>>
{
    public Task AppendToStreamAsync(
        string streamId,
        IEnumerable<EventData<ReadOnlyMemory<byte>>> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<ReadStreamResult<StoredEventData<ReadOnlyMemory<byte>>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        long? fromVersion = null,
        CancellationToken cancellationToken = default)
    {
        KurrentDBClient.ReadStreamResult readStreamResult;

        try
        {
            var kurrentDbDirection = direction == StreamReadDirection.Forward
                ? Direction.Forwards
                : Direction.Backwards;

            var kurrentDbFromVersion = fromVersion != null
                ? StreamPosition.FromInt64(fromVersion.Value)
                : StreamPosition.Start;

            readStreamResult = client.ReadStreamAsync(
                kurrentDbDirection,
                streamId,
                kurrentDbFromVersion,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            //Logger.LogError(ex, "Unable to load events from {StreamName}", streamName);
            throw;
        }

        var readState = await readStreamResult.ReadState;
        if (readState == ReadState.StreamNotFound)
        {
            return ReadStreamResult<StoredEventData<ReadOnlyMemory<byte>>>.NotFound();
        }

        return ReadStreamResult<StoredEventData<ReadOnlyMemory<byte>>>.Success(MapEventStream());

        async IAsyncEnumerable<StoredEventData<ReadOnlyMemory<byte>>> MapEventStream()
        {
            await foreach (var resolvedEvent in readStreamResult.WithCancellation(cancellationToken))
            {
                yield return new StoredEventData<ReadOnlyMemory<byte>>(
                    streamId,
                    resolvedEvent.OriginalEvent.EventNumber.ToInt64(),
                    resolvedEvent.Event.EventType,
                    resolvedEvent.Event.Data,
                    [],
                    resolvedEvent.Event.Created.Date,
                    Convert.ToInt64(resolvedEvent.OriginalEvent.Position.CommitPosition));
            }
        }
    }
}