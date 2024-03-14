using System.Runtime.CompilerServices;
using DomainBlocks.Experimental.Persistence;
using DomainBlocks.Logging;
using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Experimental.Persistence.EventStoreDb;

public sealed class EventStoreDbEventStore : IEventStore<ResolvedEvent, EventData, StreamRevision>
{
    private static readonly ILogger<EventStoreDbEventStore> Logger = Log.Create<EventStoreDbEventStore>();
    private readonly EventStoreClient _client;

    public EventStoreDbEventStore(EventStoreClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public StreamRevision NoStreamVersion => StreamRevision.None;

    public async IAsyncEnumerable<ResolvedEvent> ReadStreamAsync(
        ReadStreamDirection direction,
        string streamId,
        StreamRevision? fromVersion = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (streamId == null) throw new ArgumentNullException(nameof(streamId));

        EventStoreClient.ReadStreamResult readStreamResult;

        try
        {
            var eventStoreDirection =
                direction == ReadStreamDirection.Forwards ? Direction.Forwards : Direction.Backwards;

            var startPosition = fromVersion is null
                ? direction == ReadStreamDirection.Forwards ? StreamPosition.Start : StreamPosition.End
                : StreamPosition.FromStreamRevision(fromVersion.Value);

            // Consider reading in batches.
            readStreamResult = _client.ReadStreamAsync(
                eventStoreDirection,
                streamId,
                startPosition,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to load events from {StreamName}", streamId);
            throw;
        }

        var readState = await readStreamResult.ReadState;
        if (readState == ReadState.StreamNotFound)
        {
            yield break;
        }

        await foreach (var resolvedEvent in readStreamResult)
        {
            yield return resolvedEvent;
        }
    }

    public async Task<StreamRevision?> AppendToStreamAsync(
        string streamId,
        IEnumerable<EventData> events,
        StreamRevision? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (streamId == null) throw new ArgumentNullException(nameof(streamId));
        if (events == null) throw new ArgumentNullException(nameof(events));

        var eventsArray = events.ToArray();
        if (eventsArray.Length == 0)
        {
            Logger.LogWarning("No events in batch. Exiting");
            return null;
        }

        // Use the ID of the first event in the batch as an identifier for the whole write to ES
        var writeId = eventsArray[0].EventId;

        Logger.LogDebug(
            "Appending {EventCount} events to stream {StreamName}. Expected stream version {StreamVersion}. " +
            "Write ID {WriteId}",
            eventsArray.Length,
            streamId,
            expectedVersion,
            writeId);

        if (Logger.IsEnabled(LogLevel.Trace))
        {
            foreach (var eventData in eventsArray)
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

        IWriteResult writeResult;

        try
        {
            if (expectedVersion.HasValue)
            {
                writeResult = await _client.AppendToStreamAsync(
                    streamId,
                    expectedVersion.Value,
                    eventsArray,
                    cancellationToken: cancellationToken);
            }
            else
            {
                writeResult = await _client.AppendToStreamAsync(
                    streamId,
                    StreamState.Any,
                    eventsArray,
                    cancellationToken: cancellationToken);
            }

            Logger.LogDebug("Written events to stream. WriteId {WriteId}", writeId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to save events to stream {StreamName}. Write ID {WriteId}", streamId, writeId);
            throw;
        }

        return writeResult.NextExpectedStreamRevision;
    }
}