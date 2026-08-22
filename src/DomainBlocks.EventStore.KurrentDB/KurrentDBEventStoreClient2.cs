using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Abstractions.New;
using KurrentDB.Client;
using LogPos = KurrentDB.Client.Position;
using NativeStreamState = KurrentDB.Client.StreamState;
using StreamNotFoundException = DomainBlocks.EventStore.Abstractions.StreamNotFoundException;
using StreamPos = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public class KurrentDBEventStoreClient2<TEvent>(
    KurrentDBClient client,
    IEventEncoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventEncoder,
    IEventDecoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventDecoder) :
    IKurrentDBEventStoreClient2<TEvent>
    where TEvent : notnull
{
    public async Task AppendToStreamAsync(
        string streamId,
        ExpectedStreamState2<StreamPos> expectedState,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AppendToStreamOptions.Default;
        var kurrentExpectedState = ToNativeStreamState(expectedState);

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
            //throw new StreamAppendConflictException(streamId, options.ExpectedStreamState, actualState, ex);
            throw;
        }
    }

    public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPos, LogPos>> ReadAll(
        ReadDefinition<LogPos> definition,
        ReadAllOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPos, LogPos>> ReadStream(
        string streamId,
        ReadDefinition<StreamPos> definition,
        ReadStreamOptions2? options = null)
    {
        return ReadStreamCoreAsync(streamId, definition, options ?? ReadStreamOptions2.Default);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionDefinition<LogPos> definition,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(string streamId,
        SubscriptionDefinition<StreamPos> definition,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    private static NativeStreamState ToNativeStreamState(ExpectedStreamState2<StreamPos> expected) =>
        expected switch
        {
            { Kind: ExpectedStreamStateKind.Any } => NativeStreamState.Any,
            { Kind: ExpectedStreamStateKind.DoesNotExist } => NativeStreamState.NoStream,
            { Kind: ExpectedStreamStateKind.Exists } => NativeStreamState.StreamExists,
            { Kind: ExpectedStreamStateKind.AtVersion } => NativeStreamState.StreamRevision(expected.Version),
            _ => throw new ArgumentOutOfRangeException(nameof(expected), expected, null)
        };

    // private static NativeStreamState ToNativeStreamState(ExpectedStreamState2<StreamPos> expected) => expected switch
    // {
    //     ExpectedStreamState2<StreamPos>.Any => NativeStreamState.Any,
    //     ExpectedStreamState2<StreamPos>.DoesNotExist => NativeStreamState.NoStream,
    //     ExpectedStreamState2<StreamPos>.Exists => NativeStreamState.StreamExists,
    //     ExpectedStreamState2<StreamPos>.AtVersion(var version) => NativeStreamState.StreamRevision(version),
    //     _ => throw new ArgumentOutOfRangeException(nameof(expected), expected, null)
    // };

    private static StreamState2<StreamPos>? ToStreamState(NativeStreamState kurrentStreamState)
    {
        if (kurrentStreamState == NativeStreamState.NoStream)
            return StreamState2<StreamPos>.DoesNotExist;

        if (kurrentStreamState.HasPosition)
        {
            var value = kurrentStreamState.ToInt64();
            if (value >= 0)
                return StreamState2<StreamPos>.AtVersion(StreamPos.FromInt64(value));
        }

        return null;
    }

    private async IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPos, LogPos>> ReadStreamCoreAsync(
        string streamId,
        ReadDefinition<StreamPos> definition,
        ReadStreamOptions2 options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (direction, revision) = definition switch
        {
            ReadDefinition<StreamPos>.ForwardFromStart => (Direction.Forwards, StreamPos.Start),
            ReadDefinition<StreamPos>.BackwardFromEnd => (Direction.Backwards, StreamPos.End),
            ReadDefinition<StreamPos>.ForwardFrom d => (Direction.Forwards, d.Position),
            ReadDefinition<StreamPos>.BackwardFrom d => (Direction.Backwards, d.Position),
            _ => throw new UnreachableException($"Unknown ReadDefinition '{definition.GetType().Name}'.")
        };

        var isEmptyEnumeration = direction == Direction.Forwards && revision == StreamPos.End ||
                                 direction == Direction.Backwards && revision == StreamPos.Start;

        if (isEmptyEnumeration)
        {
            if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var result = client.ReadStreamAsync(
            direction,
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

            var (@event, metadata) = eventDecoder.Decode(record.EventType, record.Data, metadataBytes);

            yield return ReadEvent2.Create(
                @event,
                streamId,
                metadata,
                record.Created,
                originalRecord.EventNumber,
                originalRecord.Position);
        }
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var streamExist = client.ReadStreamAsync(
            Direction.Backwards,
            streamId,
            StreamPos.End,
            maxCount: 1,
            cancellationToken: cancellationToken);

        var readState = await streamExist.ReadState.ConfigureAwait(false);

        return readState == ReadState.Ok;
    }
}