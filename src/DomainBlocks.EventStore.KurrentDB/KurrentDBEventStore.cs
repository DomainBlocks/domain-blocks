using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using KurrentDB.Client;
using KurrentStreamState = KurrentDB.Client.StreamState;
using StreamNotFoundException = DomainBlocks.EventStore.Abstractions.StreamNotFoundException;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public class KurrentDBEventStore<TEvent>(
    KurrentDBClient client,
    EventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventCodec) :
    IKurrentDBEventStore<TEvent>
    where TEvent : notnull
{
    public async Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<StreamPosition>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        expectedState ??= ExpectedStreamState.Any<StreamPosition>();
        options ??= AppendOptions.Default;

        var kurrentExpectedState = ToKurrentStreamState(expectedState.Value);

        var eventData = eventCodec.Encoder
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
            var observedState = ToObservedStreamState(ex.ActualStreamState);
            throw new StreamAppendConflictException<StreamPosition>(streamId, expectedState.Value, observedState, ex);
        }
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<Position>? origin = null,
        ReadAllOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        return ReadStreamCoreAsync(streamId, direction, origin, options);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionOrigin<Position>? origin = null,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    private static KurrentStreamState ToKurrentStreamState(ExpectedStreamState<StreamPosition> expected) =>
        expected switch
        {
            { Kind: ExpectedStreamStateKind.Any } => KurrentStreamState.Any,
            { Kind: ExpectedStreamStateKind.DoesNotExist } => KurrentStreamState.NoStream,
            { Kind: ExpectedStreamStateKind.Exists } => KurrentStreamState.StreamExists,
            { Kind: ExpectedStreamStateKind.AtVersion } => KurrentStreamState.StreamRevision(expected.Version),
            _ => throw new ArgumentOutOfRangeException(nameof(expected), expected, null)
        };

    private static ObservedStreamState<StreamPosition>? ToObservedStreamState(KurrentStreamState kurrentStreamState)
    {
        if (kurrentStreamState == KurrentStreamState.NoStream)
            return ObservedStreamState.DoesNotExist<StreamPosition>();

        if (kurrentStreamState.HasPosition)
        {
            var value = kurrentStreamState.ToInt64();
            if (value >= 0)
                return ObservedStreamState.AtVersion(StreamPosition.FromInt64(value));
        }

        return null;
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadStreamCoreAsync(
        string streamId,
        ReadDirection direction,
        ReadOrigin<StreamPosition>? origin,
        ReadStreamOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        origin ??= direction == ReadDirection.Forward
            ? ReadOrigin.Start<StreamPosition>()
            : ReadOrigin.End<StreamPosition>();

        options ??= ReadStreamOptions.Default;

        if (direction.ProducesEmptyReadFrom(origin))
        {
            if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var revision = origin switch
        {
            ReadOrigin<StreamPosition>.Start => StreamPosition.Start,
            ReadOrigin<StreamPosition>.End => StreamPosition.End,
            ReadOrigin<StreamPosition>.At at => at.Position,
            _ => throw new UnreachableException($"Unknown ReadOrigin type '{origin.GetType().Name}'.")
        };

        var kurrentDirection = direction == ReadDirection.Forward ? Direction.Forwards : Direction.Backwards;

        var result = client.ReadStreamAsync(
            kurrentDirection,
            streamId,
            revision,
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

            var (@event, metadata) = eventCodec.Decoder.Decode(record.EventType, record.Data, metadataBytes);

            var context = ReadEventContext.Create(
                streamId,
                metadata,
                record.Created,
                originalRecord.EventNumber,
                originalRecord.Position);

            yield return ReadEvent.Create(@event, context);
        }
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var streamExist = client.ReadStreamAsync(
            Direction.Backwards,
            streamId,
            StreamPosition.End,
            maxCount: 1,
            cancellationToken: cancellationToken);

        var readState = await streamExist.ReadState.ConfigureAwait(false);

        return readState == ReadState.Ok;
    }
}