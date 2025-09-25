using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;
using KurrentStreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public interface IKurrentDbEventStore : IEventStoreBackend<ReadOnlyMemory<byte>>;

public class KurrentDbEventStore(KurrentDBClient client) : IKurrentDbEventStore
{
    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<ReadOnlyMemory<byte>>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        var kurrentDbStreamState = expectedState.ToKurrentDBStreamState();

        var eventData =
            events.Select(e =>
                new EventData(Uuid.NewUuid(), e.Header.EventName, e.Payload, e.Header.Metadata.SerializeToUtf8Json()));

        try
        {
            _ = await client.AppendToStreamAsync(streamId, kurrentDbStreamState, eventData,
                cancellationToken: cancellationToken);
        }
        catch (WrongExpectedVersionException e)
        {
            throw e.ToWrongExpectedStreamStateException(streamId, expectedState);
        }
    }

    public async Task<ReadStreamResult<CommittedEvent<ReadOnlyMemory<byte>>>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= ReadStreamOptions.Default;
        var direction = options.Direction;
        var position = options.Position;
        var kurrentDirection = direction == StreamReadDirection.Forward ? Direction.Forwards : Direction.Backwards;

        var revision = position switch
        {
            { IsStart: true } => KurrentStreamPosition.Start,
            { IsEnd: true } => KurrentStreamPosition.End,
            { IsSpecificVersion: true } => KurrentStreamPosition.FromInt64(position.Version.Value.ToInt64()),
            _ => default
        };

        var readStreamResult = client.ReadStreamAsync(
            kurrentDirection,
            streamId,
            revision,
            cancellationToken: cancellationToken);

        var readState = await readStreamResult.ReadState;

        return readState == ReadState.StreamNotFound
            ? ReadStreamResult.NotFound<CommittedEvent<ReadOnlyMemory<byte>>>()
            : ReadStreamResult.Success(ToCommittedEvents());

        async IAsyncEnumerable<CommittedEvent<ReadOnlyMemory<byte>>> ToCommittedEvents()
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