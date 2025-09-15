using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;
using EventRecord = DomainBlocks.EventStore.Abstractions.EventRecord;
using KurrentStreamPosition = KurrentDB.Client.StreamPosition;
using StreamPosition = DomainBlocks.EventStore.Abstractions.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public class KurrentDBEventDataStore(KurrentDBClient client) : IEventStoreBackend<ReadOnlyMemory<byte>>
{
    public Task AppendToStreamAsync(
        string streamId,
        IEnumerable<NewEventRecord<ReadOnlyMemory<byte>>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<ReadStreamResult<EventRecord<ReadOnlyMemory<byte>>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        StreamPosition? fromPosition = null,
        CancellationToken cancellationToken = default)
    {
        KurrentDBClient.ReadStreamResult readStreamResult;

        try
        {
            var kurrentDirection = direction == StreamReadDirection.Forward
                ? Direction.Forwards
                : Direction.Backwards;

            var kurrentFromVersion = KurrentStreamPosition.Start;

            if (fromPosition.HasValue)
            {
                if (fromPosition.Value.IsStart)
                    kurrentFromVersion = KurrentStreamPosition.Start;
                else if (fromPosition.Value.IsEnd)
                    kurrentFromVersion = KurrentStreamPosition.End;
                else if (fromPosition.Value.IsSpecificVersion)
                    kurrentFromVersion = KurrentStreamPosition.FromInt64(fromPosition.Value.Version.Value.ToInt64());
            }

            readStreamResult = client.ReadStreamAsync(
                kurrentDirection,
                streamId,
                kurrentFromVersion,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            //Logger.LogError(ex, "Unable to load events from {StreamName}", streamName);
            throw;
        }

        var readState = await readStreamResult.ReadState;

        return readState == ReadState.StreamNotFound
            ? ReadStreamResult.NotFound<EventRecord<ReadOnlyMemory<byte>>>()
            : ReadStreamResult.Success(MapEventStream());

        async IAsyncEnumerable<EventRecord<ReadOnlyMemory<byte>>> MapEventStream()
        {
            await foreach (var resolvedEvent in readStreamResult.WithCancellation(cancellationToken))
            {
                var header = new EventHeader(
                    streamId,
                    StreamVersion.FromInt64(resolvedEvent.OriginalEvent.EventNumber.ToInt64()),
                    resolvedEvent.Event.EventType,
                    new Dictionary<string, string>(),
                    resolvedEvent.Event.Created.Date,
                    GlobalPosition.FromUInt64(resolvedEvent.OriginalEvent.Position.CommitPosition));

                yield return EventRecord.Create(header, resolvedEvent.Event.Data);
            }
        }
    }
}