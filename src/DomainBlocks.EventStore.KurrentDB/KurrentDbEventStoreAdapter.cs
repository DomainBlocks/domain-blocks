using System.Collections.Frozen;
using System.Text.Json;
using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;
using KurrentStreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public class KurrentDbEventStoreAdapter(KurrentDBClient client) : IKurrentDbEventStoreAdapter
{
    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<ReadOnlyMemory<byte>>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        var kurrentDbStreamState = ToKurrentDbStreamState(expectedState);

        var eventData = events.Select(e =>
        {
            var serializedMetadata = JsonSerializer.SerializeToUtf8Bytes(e.Header.Metadata);
            return new EventData(Uuid.NewUuid(), e.Header.EventName, e.Payload, serializedMetadata);
        });

        try
        {
            _ = await client.AppendToStreamAsync(streamId, kurrentDbStreamState, eventData,
                cancellationToken: cancellationToken);
        }
        catch (WrongExpectedVersionException ex)
        {
            throw MapWrongExpectedVersionException(ex, streamId, expectedState);
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

        if (position.IsStart && direction == StreamReadDirection.Backward ||
            position.IsEnd && direction == StreamReadDirection.Forward)
        {
            var streamExist = client.ReadStreamAsync(
                Direction.Backwards,
                streamId,
                KurrentStreamPosition.End,
                maxCount: 1,
                cancellationToken: cancellationToken);

            var streamExistsReadState = await streamExist.ReadState;

            return streamExistsReadState == ReadState.StreamNotFound
                ? ReadStreamResult.NotFound<CommittedEvent<ReadOnlyMemory<byte>>>()
                : ReadStreamResult.Success<CommittedEvent<ReadOnlyMemory<byte>>>();
        }

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
                    FrozenDictionary<string, string>.Empty,
                    resolvedEvent.Event.Created.Date,
                    GlobalPosition.FromUInt64(resolvedEvent.OriginalEvent.Position.CommitPosition));

                yield return CommittedEvent.Create(header, resolvedEvent.Event.Data);
            }
        }
    }

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
}