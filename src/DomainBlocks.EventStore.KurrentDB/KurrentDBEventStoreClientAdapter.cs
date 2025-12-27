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
        var kurrentDbStreamState = ToKurrentDbStreamState(options.ExpectedState);

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
                    kurrentDbStreamState,
                    eventData,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WrongExpectedVersionException ex)
        {
            throw MapWrongExpectedVersionException(ex, streamId, options.ExpectedState);
        }
    }

    public async IAsyncEnumerable<ReadEvent<ReadOnlyMemory<byte>>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
            var header = new ReadEventHeader(
                streamId,
                StreamVersion.FromInt64(resolvedEvent.OriginalEvent.EventNumber.ToInt64()),
                resolvedEvent.Event.EventType,
                FrozenDictionary<string, string>.Empty,
                resolvedEvent.Event.Created.Date,
                GlobalPosition.FromUInt64(resolvedEvent.OriginalEvent.Position.CommitPosition));

            yield return ReadEvent.Create(header, resolvedEvent.Event.Data);
        }
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();

    private static WrongExpectedStreamStateException MapWrongExpectedVersionException(
        WrongExpectedVersionException ex,
        string streamId,
        ExpectedStreamState expectedStreamState)
    {
        // We request an "expectedStreamState", but kurrent returns an actual that will just be either StreamRevision,
        // or NoStream.The goal of this method is to convert from "WrongExpectedVersionException" to
        // "WrongExpectedStreamStateException", so we need to provide the actual stream state.
        var actualState = ToExpectedStreamState(ex.ActualStreamState);

        // When we expect "StreamDoesNotExist" & the stream does exist, Kurrent returns the actual stream version.
        if (expectedStreamState.IsStreamDoesNotExist &&
            expectedStreamState.IsStreamDoesNotExist != actualState.IsStreamDoesNotExist)
        {
            var streamRevision = actualState.IsSpecificVersion ? actualState.Version.Value : StreamVersion.None;
            return WrongExpectedStreamStateException.ExpectedStreamToNotExist(streamId, streamRevision);
        }

        if ((expectedStreamState.IsStreamExists || expectedStreamState.IsAny) && actualState.IsStreamDoesNotExist)
        {
            return WrongExpectedStreamStateException.ExpectedStreamToExist(streamId);
        }

        if (expectedStreamState.IsSpecificVersion)
        {
            // We have a version conflict. Either because the stream doesn't exist or the version is different.
            // So the stream revision is either None or the actual version.
            var actualStreamRevision = actualState.IsSpecificVersion ? actualState.Version.Value : StreamVersion.None;

            return WrongExpectedStreamStateException.VersionConflict(
                streamId,
                ToExpectedStreamState(ex.ExpectedStreamState),
                actualStreamRevision);
        }

        return WrongExpectedStreamStateException.Unknown(streamId, expectedStreamState, ex);
    }

    private static StreamState ToKurrentDbStreamState(ExpectedStreamState streamState) => streamState switch
    {
        _ when streamState.IsAny => StreamState.Any,
        _ when streamState.IsStreamExists => StreamState.StreamExists,
        _ when streamState.IsStreamDoesNotExist => StreamState.NoStream,
        _ when streamState.IsSpecificVersion => StreamState.StreamRevision(streamState.Version.Value.ToUint64()),
        _ => throw new ArgumentOutOfRangeException(nameof(streamState), streamState, null)
    };

    private static ExpectedStreamState ToExpectedStreamState(StreamState streamState)
    {
        return streamState switch
        {
            _ when streamState == StreamState.Any => ExpectedStreamState.Any,
            _ when streamState == StreamState.StreamExists => ExpectedStreamState.StreamExists,
            _ when streamState == StreamState.NoStream => ExpectedStreamState.StreamDoesNotExist,
            _ when streamState.HasPosition => FromVersion(streamState),
            _ => throw new ArgumentOutOfRangeException(nameof(streamState), streamState, null)
        };

        static ExpectedStreamState FromVersion(StreamState streamState)
        {
            return ExpectedStreamState.FromVersion(StreamVersion.FromInt64(streamState.ToInt64()));
        }
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