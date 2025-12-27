using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using KurrentDB.Client;
using KurrentStreamPosition = KurrentDB.Client.StreamPosition;
using StreamNotFoundException = DomainBlocks.EventStore.Abstractions.StreamNotFoundException;

namespace DomainBlocks.EventStore.KurrentDB;

public class KurrentDBEventStoreClientAdapter(KurrentDBClient client) :
    IKurrentDBEventStoreClientAdapter,
    IAsyncDisposable
{
    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<ReadOnlyMemory<byte>>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AppendToStreamOptions.Default;
        var kurrentExpectedState = ToKurrentStreamState(options.ExpectedState);

        var eventData = events.Select(e =>
        {
            var serializedMetadata = JsonSerializer.SerializeToUtf8Bytes(e.Header.Metadata);
            return new EventData(Uuid.NewUuid(), e.Header.EventName, e.Value, serializedMetadata);
        });

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
    }

    public async IAsyncEnumerable<ReadEvent<ReadOnlyMemory<byte>>> ReadStreamAsync(
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
            { IsSpecificVersion: true } => KurrentStreamPosition.FromInt64(position.Version.Value.ToInt64()),
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

            var header = new ReadEventHeader(
                streamId,
                StreamVersion.FromInt64(originalEvent.EventNumber.ToInt64()),
                @event.EventType,
                FrozenDictionary<string, string>.Empty,
                @event.Created.Date,
                GlobalPosition.FromUInt64(originalEvent.Position.CommitPosition));

            yield return ReadEvent.Create(header, @event.Data);
        }
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();

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
            var actualVersion = StreamVersion.FromInt64(actualState.ToInt64());

            if (expectedState.IsStreamDoesNotExist)
                return WrongExpectedStreamStateException.ExpectedStreamToNotExist(streamId, actualVersion);

            if (expectedState.IsSpecificVersion)
                return WrongExpectedStreamStateException.VersionConflict(streamId, expectedState, actualVersion);
        }

        return WrongExpectedStreamStateException.Unknown(streamId, expectedState, sourceException);
    }

    private static StreamState ToKurrentStreamState(ExpectedStreamState expectedState) => expectedState switch
    {
        _ when expectedState.IsAny => StreamState.Any,
        _ when expectedState.IsStreamExists => StreamState.StreamExists,
        _ when expectedState.IsStreamDoesNotExist => StreamState.NoStream,
        _ when expectedState.IsSpecificVersion => StreamState.StreamRevision(expectedState.Version.Value.ToUint64()),
        _ => throw new ArgumentOutOfRangeException(nameof(expectedState), expectedState, null)
    };

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