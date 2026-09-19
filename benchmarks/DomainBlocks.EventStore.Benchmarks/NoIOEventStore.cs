using BenchmarkDotNet.Engines;
using DomainBlocks.EventStore.Benchmarks.Proto;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.Benchmarks;

internal static class NoIOEventStore
{
    internal static readonly DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a <see cref="NoIOEventStore{TEvent,TEventData,TMetadata}"/> for <paramref name="format"/>, holding
    /// <paramref name="storedEvents"/> in their encoded form as the single stream that reads return.
    /// </summary>
    public static IEventStore<IDomainEvent, string, StreamPosition, LogPosition> Create(
        SerializationFormat format,
        params AppendableEvent<IDomainEvent>[] storedEvents)
    {
        return format switch
        {
            SerializationFormat.Json => Create(
                new JsonObjectSerializer(),
                new JsonMetadataSerializer(),
                format,
                storedEvents),

            SerializationFormat.JsonUtf8 => Create<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>(
                new JsonUtf8BytesObjectSerializer(),
                new JsonUtf8BytesMetadataSerializer(),
                format,
                storedEvents),

            SerializationFormat.Bson => Create<BsonValue, BsonValue>(
                new BsonDocumentObjectSerializer(),
                new BsonDocumentMetadataSerializer(),
                format,
                storedEvents),

            SerializationFormat.RawBson => Create(
                new RawBsonObjectSerializer(),
                new RawBsonMetadataSerializer(),
                format,
                storedEvents),

            SerializationFormat.Protobuf => Create<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>(
                new ProtobufBytesObjectSerializer(),
                new JsonUtf8BytesMetadataSerializer(),
                format,
                storedEvents),

            SerializationFormat.ProtobufJson => Create(
                new ProtobufJsonObjectSerializer(),
                new JsonMetadataSerializer(),
                format,
                storedEvents),

            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static NoIOEventStore<IDomainEvent, TEventData, TMetadata> Create<TEventData, TMetadata>(
        IObjectSerializer<TEventData> eventSerializer,
        IMetadataSerializer<TMetadata> metadataSerializer,
        SerializationFormat format,
        AppendableEvent<IDomainEvent>[] storedEvents)
        where TEventData : notnull
    {
        var codecOptions = new EventCodecOptions<IDomainEvent, TEventData, TMetadata>
        {
            // Both event types are stored under the same name, so that name lookups cost the same for every format.
            TypeMap = EventTypeMap.Create(
                format.IsProtobuf
                    ? EventTypeMapping.ReadWrite<ProtoTestEvent>(nameof(TestEvent))
                    : EventTypeMapping.ReadWrite<TestEvent>()),
            EventSerializer = eventSerializer,
            MetadataSerializer = metadataSerializer
        };

        var codec = EventCodec.Create(codecOptions);

        return new NoIOEventStore<IDomainEvent, TEventData, TMetadata>(codec, codec.Encode(storedEvents).ToArray());
    }
}

/// <summary>
/// An event store with everything on the append and read paths of a real store except the I/O. Appends encode their
/// events and discard the result. Reads decode the encoded events the store was created with, and build a
/// <see cref="ReadEvent{TPayload,TStreamId,TStreamPos,TLogPos}"/> and its context for each, as a real store does for
/// each row or document it fetches.
/// </summary>
internal sealed class NoIOEventStore<TEvent, TEventData, TMetadata>(
    IEventCodec<TEvent, TEventData, TMetadata> eventCodec,
    EncodedEvent<TEventData, TMetadata>[] storedEvents) :
    IEventStore<TEvent, string, StreamPosition, LogPosition>
    where TEvent : notnull
    where TEventData : notnull
{
    private readonly Consumer _consumer = new();

    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<StreamPosition> expectedState = default,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var (eventName, eventData, metadata) in eventCodec.Encode(events))
        {
            _consumer.Consume(eventName);
            _consumer.Consume(in eventData);
            _consumer.Consume(in metadata);
        }

        return Task.CompletedTask;
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition> origin = default,
        ReadAllOptions? options = null)
    {
        throw new NotSupportedException();
    }

    // ReSharper disable once AsyncMethodWithoutAwait
    public async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition> origin = default,
        ReadStreamOptions? options = null)
    {
        options ??= ReadStreamOptions.Default;

        for (var i = 0; i < storedEvents.Length; i++)
        {
            var (eventName, eventData, storedMetadata) = storedEvents[i];

            // A real store leaves excluded metadata out of its query, so the decoder never sees it.
            var (payload, metadata) = eventCodec.Decode(
                eventName,
                eventData,
                options.IncludeMetadata ? storedMetadata : default);

            var context = ReadEventContext.Create(
                streamId,
                eventName,
                metadata,
                NoIOEventStore.CreatedAt,
                StreamPosition.FromInt64(i),
                LogPosition.FromInt64(i));

            yield return ReadEvent.Create(payload, context);
        }
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToAll(
        SubscriptionOrigin<LogPosition> origin = default,
        SubscriptionOptions? options = null)
    {
        throw new NotSupportedException();
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition> origin = default,
        SubscriptionOptions? options = null)
    {
        throw new NotSupportedException();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}