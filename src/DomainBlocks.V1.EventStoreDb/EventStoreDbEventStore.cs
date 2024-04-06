using System.Runtime.CompilerServices;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Exceptions;
using DomainBlocks.Logging;
using DomainBlocks.V1.Abstractions.Subscriptions;
using DomainBlocks.V1.EventStoreDb.Extensions;
using EventStore.Client;
using Microsoft.Extensions.Logging;
using StreamPosition = DomainBlocks.V1.Abstractions.StreamPosition;
using WrongExpectedVersionException = DomainBlocks.V1.Abstractions.Exceptions.WrongExpectedVersionException;

namespace DomainBlocks.V1.EventStoreDb;

public sealed class EventStoreDbEventStore : IEventStore
{
    private static readonly ILogger<EventStoreDbEventStore> Logger = LogProvider.Get<EventStoreDbEventStore>();
    private readonly EventStoreClient _client;

    public EventStoreDbEventStore(EventStoreClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public IAsyncEnumerable<StoredEventEntry> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamPosition fromPosition,
        CancellationToken cancellationToken = default)
    {
        return ReadStreamInternalAsync(streamName, direction, fromPosition, null, cancellationToken);
    }

    public IAsyncEnumerable<StoredEventEntry> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamReadOrigin readOrigin = StreamReadOrigin.Default,
        CancellationToken cancellationToken = default)
    {
        return ReadStreamInternalAsync(streamName, direction, null, readOrigin, cancellationToken);
    }

    public IEventStreamSubscription SubscribeToAll(GlobalPosition? afterPosition = null)
    {
        throw new NotImplementedException();
    }

    public IEventStreamSubscription SubscribeToStream(string streamName, StreamPosition? afterPosition = null)
    {
        throw new NotImplementedException();
    }

    public Task<StreamPosition?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WritableEventEntry> events,
        StreamPosition expectedVersion,
        CancellationToken cancellationToken = default)
    {
        return AppendToStreamInternalAsync(streamName, events, expectedVersion, null, cancellationToken);
    }

    public Task<StreamPosition?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WritableEventEntry> events,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default)
    {
        return AppendToStreamInternalAsync(streamName, events, null, expectedState, cancellationToken);
    }

    private async IAsyncEnumerable<StoredEventEntry> ReadStreamInternalAsync(
        string streamName,
        StreamReadDirection direction,
        StreamPosition? fromVersion,
        StreamReadOrigin? readOrigin,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EventStoreClient.ReadStreamResult readStreamResult;

        try
        {
            var eventStoreDbDirection =
                direction == StreamReadDirection.Forwards ? Direction.Forwards : Direction.Backwards;

            var eventStoreDbFromVersion = fromVersion != null
                ? new EventStore.Client.StreamPosition(fromVersion.Value.ToUInt64())
                : readOrigin!.Value.ToEventStoreDbStreamPosition(direction);

            readStreamResult = _client.ReadStreamAsync(
                eventStoreDbDirection,
                streamName,
                eventStoreDbFromVersion,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to load events from {StreamName}", streamName);
            throw;
        }

        var readState = await readStreamResult.ReadState;
        if (readState == ReadState.StreamNotFound)
        {
            yield break;
        }

        await foreach (var resolvedEvent in readStreamResult)
        {
            var streamPosition = new StreamPosition(resolvedEvent.OriginalEvent.EventNumber);
            var globalPosition = new GlobalPosition(resolvedEvent.OriginalEvent.Position.CommitPosition);

            yield return new StoredEventEntry(
                resolvedEvent.Event.EventType,
                resolvedEvent.Event.Data,
                resolvedEvent.Event.Metadata,
                streamPosition,
                globalPosition);
        }
    }

    private async Task<StreamPosition?> AppendToStreamInternalAsync(
        string streamName,
        IEnumerable<WritableEventEntry> events,
        StreamPosition? expectedVersion,
        ExpectedStreamState? expectedState,
        CancellationToken cancellationToken)
    {
        var eventsArray = events.ToArray();
        if (eventsArray.Length == 0)
        {
            Logger.LogWarning("No events in batch. Exiting");
            return null;
        }

        var convertedEvents = eventsArray
            .Select(x => new EventData(Uuid.NewUuid(), x.Name, x.Payload, x.Metadata))
            .ToArray();

        // Use the ID of the first event in the batch as an identifier for the whole write to ES
        var writeId = convertedEvents[0].EventId;

        Logger.LogDebug(
            "Appending {EventCount} events to stream {StreamName}. Expected stream version {StreamVersion}. " +
            "Write ID {WriteId}",
            eventsArray.Length,
            streamName,
            expectedVersion?.ToString() ?? expectedState.ToString(),
            writeId);

        if (Logger.IsEnabled(LogLevel.Trace))
        {
            foreach (var eventData in convertedEvents)
            {
                Logger.LogTrace(
                    "Event to append {EventId}. EventType {EventType}. WriteId {WriteId}. EventBytes {EventBytes}. " +
                    "MetadataBytes {MetadataBytes}. ContentType {ContentType} ",
                    eventData.EventId,
                    eventData.Type,
                    writeId,
                    eventData.Data,
                    eventData.Metadata,
                    eventData.ContentType);
            }
        }

        try
        {
            IWriteResult writeResult;

            if (expectedVersion.HasValue)
            {
                writeResult = await _client.AppendToStreamAsync(
                    streamName,
                    expectedVersion.Value.ToUInt64(),
                    convertedEvents,
                    cancellationToken: cancellationToken);
            }
            else
            {
                writeResult = await _client.AppendToStreamAsync(
                    streamName,
                    expectedState!.Value.ToEventStoreDbStreamState(),
                    convertedEvents,
                    cancellationToken: cancellationToken);
            }

            Logger.LogDebug("Written events to stream. WriteId {WriteId}", writeId);

            return new StreamPosition(writeResult.NextExpectedStreamRevision);
        }
        catch (EventStore.Client.WrongExpectedVersionException ex)
        {
            var actualVersion = new StreamPosition(ex.ActualStreamRevision);

            if (expectedVersion.HasValue)
                throw new WrongExpectedVersionException(
                    ErrorMessages.WrongExpectedVersion(streamName, expectedVersion.Value, actualVersion), ex);

            throw new WrongExpectedVersionException(
                ErrorMessages.WrongExpectedVersion(streamName, expectedState!.Value, actualVersion), ex);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex, "Unable to save events to stream {StreamName}. Write ID {WriteId}", streamName, writeId);

            throw;
        }
    }
}