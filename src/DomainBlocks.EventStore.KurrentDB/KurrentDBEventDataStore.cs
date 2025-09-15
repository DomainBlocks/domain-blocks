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
        var kurrentDirection = direction == StreamReadDirection.Forward ? Direction.Forwards : Direction.Backwards;

        var revision = fromPosition switch
        {
            { IsStart: true } => KurrentStreamPosition.Start,
            { IsEnd: true } => KurrentStreamPosition.End,
            { IsSpecificVersion: true } pos => KurrentStreamPosition.FromInt64(pos.Version.Value.ToInt64()),
            _ => direction == StreamReadDirection.Forward ? KurrentStreamPosition.Start : KurrentStreamPosition.End
        };

        var readStreamResult = client.ReadStreamAsync(
            kurrentDirection,
            streamId,
            revision,
            cancellationToken: cancellationToken);

        var readState = await readStreamResult.ReadState;

        return readState == ReadState.StreamNotFound
            ? ReadStreamResult.NotFound<EventRecord<ReadOnlyMemory<byte>>>()
            : ReadStreamResult.Success(MapEventStream());

        async IAsyncEnumerable<EventRecord<ReadOnlyMemory<byte>>> MapEventStream()
        {
            await foreach (var resolvedEvent in readStreamResult)
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