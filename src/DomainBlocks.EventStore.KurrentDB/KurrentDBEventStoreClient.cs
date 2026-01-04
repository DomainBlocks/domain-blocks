using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;
using KurrentStreamPosition = KurrentDB.Client.StreamPosition;
using StreamNotFoundException = DomainBlocks.EventStore.Abstractions.StreamNotFoundException;

namespace DomainBlocks.EventStore.KurrentDB;

public class KurrentDBEventStoreClient<TEventBase>(
    KurrentDBClient client,
    IEventCodec<TEventBase, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventCodec) :
    IEventStoreClient<TEventBase>
    where TEventBase : class
{
    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEventBase>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AppendToStreamOptions.Default;
        var kurrentExpectedState = ToKurrentStreamState(options.ExpectedState);
        var eventData = EncodeEvents(events, eventCodec.CreateEncoder());

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
            throw MapWrongExpectedVersionException(streamId, options.ExpectedState, ex);
        }

        return;

        static IEnumerable<EventData> EncodeEvents(
            IEnumerable<AppendEvent<TEventBase>> sourceEvents,
            IEventEncoder<TEventBase, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> encoder)
        {
            foreach (var e in sourceEvents)
            {
                var (eventName, eventData, metadata) = encoder.Encode(e);
                yield return new EventData(Uuid.NewUuid(), eventName, eventData, metadata);
            }
        }
    }

    public async IAsyncEnumerable<ReadEvent<TEventBase>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= ReadStreamOptions.Default;
        var position = options.Position;
        var direction = options.Direction;
        var kurrentDirection = direction == StreamReadDirection.Forward ? Direction.Forwards : Direction.Backwards;

        var revision = position switch
        {
            { IsStart: true } => KurrentStreamPosition.Start,
            { IsEnd: true } => KurrentStreamPosition.End,
            { IsSpecificVersion: true } => KurrentStreamPosition.FromStreamRevision(position.Version.Value.Value),
            _ => default
        };

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
            var @event = resolvedEvent.Event;
            var originalEvent = resolvedEvent.OriginalEvent;

            var context = new ReadEventContext(
                streamId,
                new StreamVersion(originalEvent.EventNumber.ToUInt64()),
                @event.Created,
                new GlobalPosition(originalEvent.Position.CommitPosition));

            var metadata = options.IncludeMetadata ? @event.Metadata : default;

            yield return eventCodec.Decode(@event.EventType, @event.Data, metadata, context);
        }
    }

    private static StreamState ToKurrentStreamState(ExpectedStreamState expectedState) => expectedState switch
    {
        _ when expectedState.IsAny => StreamState.Any,
        _ when expectedState.IsStreamExists => StreamState.StreamExists,
        _ when expectedState.IsStreamDoesNotExist => StreamState.NoStream,
        _ when expectedState.IsSpecificVersion => StreamState.StreamRevision(expectedState.Version.Value.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(expectedState), expectedState, null)
    };

    private static WrongExpectedStreamStateException MapWrongExpectedVersionException(
        string streamId,
        ExpectedStreamState expectedState,
        WrongExpectedVersionException sourceException)
    {
        var actualState = sourceException.ActualStreamState;

        if (expectedState.IsStreamExists && actualState == StreamState.NoStream)
            return WrongExpectedStreamStateException.ExpectedStreamToExist(streamId);

        if (actualState.HasPosition)
        {
            // Consider adding validation here rather than trusting Kurrent to be correct.
            var actualVersion = new StreamVersion((ulong)actualState.ToInt64());

            if (expectedState.IsStreamDoesNotExist)
                return WrongExpectedStreamStateException.ExpectedStreamToNotExist(streamId, actualVersion);

            if (expectedState.IsSpecificVersion)
                return WrongExpectedStreamStateException.VersionConflict(streamId, expectedState, actualVersion);
        }

        // Revisit "Unknown".
        return WrongExpectedStreamStateException.Unknown(streamId, expectedState, sourceException);
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