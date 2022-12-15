using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Core;
using DomainBlocks.Core.Serialization;
using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Persistence.EventStore;

public class EventStoreEventsRepository : IEventsRepository
{
    private static readonly ILogger<EventStoreEventsRepository> Log = Logger.CreateFor<EventStoreEventsRepository>();
    private readonly EventStoreClient _client;
    private readonly IEventConverter<EventRecord, EventData> _eventConverter;

    public EventStoreEventsRepository(EventStoreClient client, IEventConverter<EventRecord, EventData> eventConverter)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _eventConverter = eventConverter ?? throw new ArgumentNullException(nameof(eventConverter));

        Log.LogDebug(
            "EventStoreEventsRepository created using {EventConverterType} serializer",
            eventConverter.GetType().Name);
    }

    public async Task<long> SaveEventsAsync(
        string streamName,
        long expectedStreamVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
    {
        if (streamName == null) throw new ArgumentNullException(nameof(streamName));
        if (events == null) throw new ArgumentNullException(nameof(events));

        EventData[] eventDataArray;
        try
        {
            eventDataArray = events.Select(e => _eventConverter.SerializeToWriteEvent(e)).ToArray();
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Unable to serialize events. Aborting write to stream {StreamName}", streamName);
            throw;
        }

        var streamVersion = MapStreamVersionToEventStoreStreamRevision(expectedStreamVersion);
        if (eventDataArray.Length == 0)
        {
            Log.LogWarning("No events in batch. Exiting");
            return streamVersion.ToInt64();
        }

        // Use the ID of the first event in the batch as an identifier for the whole write to ES
        var writeId = eventDataArray[0].EventId;

        Log.LogDebug(
            "Appending {EventCount} events to stream {StreamName}. Expected stream version {StreamVersion}. " +
            "Write ID {WriteId}",
            eventDataArray.Length,
            streamName,
            streamVersion,
            writeId);

        if (Log.IsEnabled(LogLevel.Trace))
        {
            foreach (var eventData in eventDataArray)
            {
                Log.LogTrace(
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

        IWriteResult writeResult;
        try
        {
            writeResult = await _client.AppendToStreamAsync(
                streamName,
                streamVersion,
                eventDataArray,
                cancellationToken: cancellationToken);

            Log.LogDebug("Written events to stream. WriteId {WriteId}", writeId);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Unable to save events to stream {StreamName}. Write Id {WriteId}", streamName, writeId);
            throw;
        }

        return writeResult.NextExpectedStreamRevision.ToInt64();
    }

    public async IAsyncEnumerable<object> LoadEventsAsync(
        string streamName,
        long startPosition = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (streamName == null) throw new ArgumentNullException(nameof(streamName));

        EventStoreClient.ReadStreamResult readStreamResult;

        try
        {
            readStreamResult = _client.ReadStreamAsync(
                Direction.Forwards,
                streamName,
                StreamPosition.FromInt64(startPosition),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Unable to load events from {StreamName}", streamName);
            throw;
        }

        var readState = await readStreamResult.ReadState;
        if (readState == ReadState.StreamNotFound)
        {
            yield break;
        }

        await foreach (var resolvedEvent in readStreamResult)
        {
            object deserializedEvent;

            try
            {
                deserializedEvent =
                    await _eventConverter.DeserializeEvent(resolvedEvent.OriginalEvent, cancellationToken: cancellationToken);
            }
            catch (EventDeserializeException ex)
            {
                Log.LogWarning(ex, "Error deserializing event. This may cause data inconsistencies");
                continue;
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Unable to load events from {StreamName}", streamName);
                throw;
            }

            yield return deserializedEvent;
        }
    }

    private static StreamRevision MapStreamVersionToEventStoreStreamRevision(long streamVersion)
    {
        return streamVersion switch
        {
            StreamVersion.NewStream => StreamRevision.None, _ => StreamRevision.FromInt64(streamVersion)
        };
    }
}