using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;
using KurrentStreamPosition = KurrentDB.Client.StreamPosition;
using KurrentStreamState = KurrentDB.Client.StreamState;
using StreamNotFoundException = DomainBlocks.EventStore.Abstractions.StreamNotFoundException;
using StreamState = DomainBlocks.EventStore.Abstractions.StreamState;

namespace DomainBlocks.EventStore.KurrentDB;

public class KurrentDBEventStoreClient<TEvent>(
    KurrentDBClient client,
    IEventEncoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventEncoder,
    IEventDecoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventDecoder) :
    IEventStoreClient<TEvent>
    where TEvent : notnull
{
    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AppendToStreamOptions();
        var kurrentExpectedState = ToKurrentStreamState(options.ExpectedStreamState);

        var eventData = eventEncoder
            .Encode(events)
            .Select(x => new EventData(Uuid.NewUuid(), x.EventName, x.EventData, x.Metadata));

        try
        {
            _ = await client
                .AppendToStreamAsync(
                    streamId,
                    kurrentExpectedState,
                    eventData,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WrongExpectedVersionException ex)
        {
            var actualState = ToStreamState(ex.ActualStreamState);
            throw new StreamAppendConflictException(streamId, options.ExpectedStreamState, actualState, ex);
        }
    }

    public async IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= ReadStreamOptions.Default;
        var position = options.Position;
        var direction = options.Direction;

        if (position.IsStart && direction == StreamReadDirection.Backward ||
            position.IsEnd && direction == StreamReadDirection.Forward)
        {
            if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var kurrentDirection = ToKurrentDirection(direction);
        var kurrentPosition = ToKurrentStreamPosition(position);

        var result = client.ReadStreamAsync(
            kurrentDirection,
            streamId,
            kurrentPosition,
            cancellationToken: cancellationToken);

        if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
            await result.ReadState.ConfigureAwait(false) == ReadState.StreamNotFound)
        {
            throw new StreamNotFoundException(streamId);
        }

        await foreach (var resolvedEvent in result.ConfigureAwait(false))
        {
            var record = resolvedEvent.Event;
            var originalRecord = resolvedEvent.OriginalEvent;
            var metadataBytes = options.IncludeMetadata ? record.Metadata : default;

            var (@event, metadata) = eventDecoder.Decode(record.EventType, record.Data, metadataBytes);

            var streamVersion = new StreamVersion(originalRecord.EventNumber.ToUInt64());
            var globalPosition = new LogPosition(originalRecord.Position.CommitPosition);

            var context = new ReadEventContext(streamId, streamVersion, record.Created, globalPosition);

            yield return ReadEvent.Create(@event, metadata, context);
        }
    }

    private static KurrentStreamState ToKurrentStreamState(ExpectedStreamState expected) => expected switch
    {
        _ when expected.IsAny => KurrentStreamState.Any,
        _ when expected.IsStreamExists => KurrentStreamState.StreamExists,
        _ when expected.IsStreamDoesNotExist => KurrentStreamState.NoStream,
        _ when expected.IsSpecificVersion => KurrentStreamState.StreamRevision(expected.Version.Value.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(expected), expected, null)
    };

    private static Direction ToKurrentDirection(StreamReadDirection direction)
    {
        return direction == StreamReadDirection.Forward ? Direction.Forwards : Direction.Backwards;
    }

    private static KurrentStreamPosition ToKurrentStreamPosition(StreamReadPosition position)
    {
        return position switch
        {
            { IsStart: true } => KurrentStreamPosition.Start,
            { IsEnd: true } => KurrentStreamPosition.End,
            { IsSpecificVersion: true } => KurrentStreamPosition.FromStreamRevision(position.Version.Value.Value),
            _ => default
        };
    }

    private static StreamState? ToStreamState(KurrentStreamState kurrentStreamState)
    {
        if (kurrentStreamState == KurrentStreamState.NoStream)
            return StreamState.StreamDoesNotExist;

        if (kurrentStreamState.HasPosition)
        {
            var value = kurrentStreamState.ToInt64();
            if (value >= 0)
                return StreamState.StreamExists(StreamVersion.FromInt64(value));
        }

        return null;
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var streamExist = client.ReadStreamAsync(
            Direction.Backwards,
            streamId,
            KurrentStreamPosition.End,
            maxCount: 1,
            cancellationToken: cancellationToken);

        var readState = await streamExist.ReadState.ConfigureAwait(false);

        return readState == ReadState.Ok;
    }
}