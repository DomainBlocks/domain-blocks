using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;
using KurrentStreamPosition = KurrentDB.Client.StreamPosition;
using StreamPosition = DomainBlocks.EventStore.Abstractions.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public class KurrentDBEventDataStore(KurrentDBClient client) : IEventStoreBackend<ReadOnlyMemory<byte>>
{
    public Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<ReadOnlyMemory<byte>>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<ReadStreamResult<CommittedEvent<ReadOnlyMemory<byte>>>> ReadStreamAsync(
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

        if (readState == ReadState.StreamNotFound)
            return ReadStreamResult.NotFound<CommittedEvent<ReadOnlyMemory<byte>>>();
        
        return ReadStreamResult.Success(MapEventStream());

        async IAsyncEnumerable<CommittedEvent<ReadOnlyMemory<byte>>> MapEventStream()
        {
            await foreach (var resolvedEvent in readStreamResult)
            {
                var header = new CommittedEventHeader(
                    streamId,
                    StreamVersion.FromInt64(resolvedEvent.OriginalEvent.EventNumber.ToInt64()),
                    resolvedEvent.Event.EventType,
                    new Dictionary<string, string>(),
                    resolvedEvent.Event.Created.Date,
                    GlobalPosition.FromUInt64(resolvedEvent.OriginalEvent.Position.CommitPosition));

                yield return CommittedEvent.Create(header, resolvedEvent.Event.Data);
            }
        }
    }
}