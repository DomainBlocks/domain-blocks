using System.Runtime.CompilerServices;
using DomainBlocks.Persistence.Events;
using DomainBlocks.Logging;
using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Persistence.EventStoreDb;

public sealed class EventStoreDbEventStore : IEventStore
{
    private static readonly ILogger<EventStoreDbEventStore> Logger = Log.Create<EventStoreDbEventStore>();
    private readonly EventStoreClient _client;

    public EventStoreDbEventStore(EventStoreClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public IAsyncEnumerable<ReadEvent> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamVersion fromVersion,
        CancellationToken cancellationToken = default)
    {
        var fromVersionInternal = new StreamPosition(fromVersion.ToUInt64());
        return ReadStreamInternalAsync(streamName, direction, fromVersionInternal, cancellationToken);
    }

    public IAsyncEnumerable<ReadEvent> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamReadOrigin readOrigin = StreamReadOrigin.Default,
        CancellationToken cancellationToken = default)
    {
        var fromVersion = readOrigin switch
        {
            StreamReadOrigin.Default => direction == StreamReadDirection.Forwards
                ? StreamPosition.Start
                : StreamPosition.End,
            StreamReadOrigin.Start => StreamPosition.Start,
            StreamReadOrigin.End => StreamPosition.End,
            _ => throw new ArgumentOutOfRangeException(nameof(readOrigin), readOrigin, null)
        };

        return ReadStreamInternalAsync(streamName, direction, fromVersion, cancellationToken);
    }

    public Task<StreamVersion?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WriteEvent> events,
        StreamVersion expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var expectedVersionInternal = new StreamRevision(expectedVersion.ToUInt64());
        return AppendToStreamInternalAsync(streamName, events, expectedVersionInternal, null, cancellationToken);
    }

    public Task<StreamVersion?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WriteEvent> events,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default)
    {
        var expectedStateInternal = expectedState switch
        {
            ExpectedStreamState.Any => StreamState.Any,
            ExpectedStreamState.NoStream => StreamState.NoStream,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedState), expectedState, null)
        };

        return AppendToStreamInternalAsync(streamName, events, null, expectedStateInternal, cancellationToken);
    }

    private async IAsyncEnumerable<ReadEvent> ReadStreamInternalAsync(
        string streamName,
        StreamReadDirection direction,
        StreamPosition fromVersion,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EventStoreClient.ReadStreamResult readStreamResult;

        try
        {
            var eventStoreDbDirection =
                direction == StreamReadDirection.Forwards ? Direction.Forwards : Direction.Backwards;

            // Consider reading in batches.
            readStreamResult = _client.ReadStreamAsync(
                eventStoreDbDirection,
                streamName,
                fromVersion,
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
            var streamVersion = StreamVersion.FromUInt64(resolvedEvent.OriginalEventNumber);

            yield return new ReadEvent(
                resolvedEvent.Event.EventType, resolvedEvent.Event.Data, resolvedEvent.Event.Metadata, streamVersion);
        }
    }

    private async Task<StreamVersion?> AppendToStreamInternalAsync(
        string streamName,
        IEnumerable<WriteEvent> events,
        StreamRevision? expectedVersion,
        StreamState? expectedState,
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
                    expectedVersion.Value,
                    convertedEvents,
                    cancellationToken: cancellationToken);
            }
            else
            {
                writeResult = await _client.AppendToStreamAsync(
                    streamName,
                    expectedState!.Value,
                    convertedEvents,
                    cancellationToken: cancellationToken);
            }

            Logger.LogDebug("Written events to stream. WriteId {WriteId}", writeId);

            return StreamVersion.FromUInt64(writeResult.NextExpectedStreamRevision);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex, "Unable to save events to stream {StreamName}. Write ID {WriteId}", streamName, writeId);

            throw;
        }
    }
}