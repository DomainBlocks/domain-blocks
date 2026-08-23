using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using KurrentDB.Client;
using NativeStreamState = KurrentDB.Client.StreamState;
using StreamNotFoundException = DomainBlocks.EventStore.Abstractions.StreamNotFoundException;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public class KurrentDBEventStoreClient<TEvent>(
    KurrentDBClient client,
    IEventEncoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventEncoder,
    IEventDecoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventDecoder) :
    IKurrentDBEventStoreClient<TEvent>
    where TEvent : notnull
{
    public async Task AppendToStreamAsync(
        string streamId,
        ExpectedStreamState<StreamPosition> expectedState,
        IEnumerable<AppendEvent<TEvent>> events,
        Guid? commitId = null,
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

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadAll(
        ReadDefinition<Position> definition,
        ReadAllOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadStream(
        string streamId,
        ReadDefinition<StreamPosition> definition,
        ReadStreamOptions? options = null)
    {
        return ReadStreamCoreAsync(streamId, definition, options ?? ReadStreamOptions.Default);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionDefinition<Position> definition,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(string streamId,
        SubscriptionDefinition<StreamPosition> definition,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    private static NativeStreamState ToNativeStreamState(ExpectedStreamState<StreamPosition> expected) =>
        expected switch
        {
            { Kind: ExpectedStreamStateKind.Any } => NativeStreamState.Any,
            { Kind: ExpectedStreamStateKind.DoesNotExist } => NativeStreamState.NoStream,
            { Kind: ExpectedStreamStateKind.Exists } => NativeStreamState.StreamExists,
            { Kind: ExpectedStreamStateKind.AtVersion } => NativeStreamState.StreamRevision(expected.Version),
            _ => throw new ArgumentOutOfRangeException(nameof(expected), expected, null)
        };

    private static StreamState<StreamPosition>? ToStreamState(NativeStreamState kurrentStreamState)
    {
        if (kurrentStreamState == NativeStreamState.NoStream)
            return StreamState<StreamPosition>.DoesNotExist;

        if (kurrentStreamState.HasPosition)
        {
            var value = kurrentStreamState.ToInt64();
            if (value >= 0)
                return StreamState<StreamPosition>.AtVersion(StreamPosition.FromInt64(value));
        }

        return null;
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadStreamCoreAsync(
        string streamId,
        ReadDefinition<StreamPosition> definition,
        ReadStreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (direction, revision) = definition switch
        {
            ReadDefinition<StreamPosition>.ForwardFromStart => (Direction.Forwards, StreamPosition.Start),
            ReadDefinition<StreamPosition>.BackwardFromEnd => (Direction.Backwards, StreamPosition.End),
            ReadDefinition<StreamPosition>.ForwardFrom d => (Direction.Forwards, d.Position),
            ReadDefinition<StreamPosition>.BackwardFrom d => (Direction.Backwards, d.Position),
            _ => throw new UnreachableException($"Unknown ReadDefinition type '{definition.GetType().Name}'.")
        };

        var isEmptyEnumeration = direction == Direction.Forwards && revision == StreamPosition.End ||
                                 direction == Direction.Backwards && revision == StreamPosition.Start;

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

            yield return ReadEvent.Create(
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
            StreamPosition.End,
            maxCount: 1,
            cancellationToken: cancellationToken);

        var readState = await streamExist.ReadState.ConfigureAwait(false);

        return readState == ReadState.Ok;
    }
}