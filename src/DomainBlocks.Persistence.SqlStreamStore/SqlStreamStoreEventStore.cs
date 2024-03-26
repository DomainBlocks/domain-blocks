using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using DomainBlocks.Persistence.Events;
using DomainBlocks.Logging;
using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using Microsoft.Extensions.Logging;
using SqlStreamStoreStreamVersion = DomainBlocks.ThirdParty.SqlStreamStore.Streams.StreamVersion;

namespace DomainBlocks.Persistence.SqlStreamStore;

public sealed class SqlStreamStoreEventStore : IEventStore
{
    private static readonly ILogger<SqlStreamStoreEventStore> Logger = Log.Create<SqlStreamStoreEventStore>();
    private readonly IStreamStore _streamStore;
    private readonly int _readPageSize;

    public SqlStreamStoreEventStore(IStreamStore streamStore, int readPageSize = 600)
    {
        _streamStore = streamStore;
        _readPageSize = readPageSize;
    }

    public IAsyncEnumerable<ReadEvent> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamVersion fromVersion,
        CancellationToken cancellationToken = default)
    {
        var fromVersionInternal = Convert.ToInt32(fromVersion.ToUInt64());
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
                ? SqlStreamStoreStreamVersion.Start
                : SqlStreamStoreStreamVersion.End,
            StreamReadOrigin.Start => SqlStreamStoreStreamVersion.Start,
            StreamReadOrigin.End => SqlStreamStoreStreamVersion.End,
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
        var expectedVersionInternal = Convert.ToInt32(expectedVersion.ToUInt64());
        return AppendToStreamInternalAsync(streamName, events, expectedVersionInternal, cancellationToken);
    }

    public Task<StreamVersion?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WriteEvent> events,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default)
    {
        var expectedVersion = expectedState switch
        {
            ExpectedStreamState.Any => ExpectedVersion.Any,
            ExpectedStreamState.NoStream => ExpectedVersion.NoStream,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedState), expectedState, null)
        };

        return AppendToStreamInternalAsync(streamName, events, expectedVersion, cancellationToken);
    }

    private async IAsyncEnumerable<ReadEvent> ReadStreamInternalAsync(
        string streamName,
        StreamReadDirection direction,
        int fromVersion,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Get the first page.
        ReadStreamPage page;
        try
        {
            if (direction == StreamReadDirection.Forwards)
            {
                page = await _streamStore.ReadStreamForwards(
                    streamName, fromVersion, _readPageSize, cancellationToken: cancellationToken);
            }
            else
            {
                page = await _streamStore.ReadStreamBackwards(
                    streamName, fromVersion, _readPageSize, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to load events from {StreamName}", streamName);
            throw;
        }

        while (true)
        {
            foreach (var message in page.Messages)
            {
                var payload = await message.GetJsonData(cancellationToken);
                var payloadAsBytes = Encoding.UTF8.GetBytes(payload);
                var metadataAsBytes =
                    message.JsonMetadata == null ? null : Encoding.UTF8.GetBytes(message.JsonMetadata);
                var streamVersion = StreamVersion.FromUInt64(Convert.ToUInt64(message.StreamVersion));
                yield return new ReadEvent(message.Type, payloadAsBytes, metadataAsBytes, streamVersion);
            }

            if (page.IsEnd)
            {
                break;
            }

            // Get the next page.
            try
            {
                page = await page.ReadNext(cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unable to load events from {StreamName}", streamName);
                throw;
            }
        }
    }

    private async Task<StreamVersion?> AppendToStreamInternalAsync(
        string streamName,
        IEnumerable<WriteEvent> events,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var eventsArray = events.ToArray();
        if (eventsArray.Length == 0)
        {
            Logger.LogWarning("No events in batch. Exiting");
            return null;
        }

        var convertedEvents = eventsArray
            .Select(x => new NewStreamMessage(
                Guid.NewGuid(),
                x.Name,
                Encoding.UTF8.GetString(x.Payload.Span),
                x.Metadata == null ? null : Encoding.UTF8.GetString(x.Metadata.Value.Span)))
            .ToArray();

        // Use the ID of the first event in the batch as an identifier for the whole write
        var writeId = convertedEvents[0].MessageId;

        Logger.LogDebug(
            "Appending {EventCount} events to stream {StreamName}. Expected stream version {StreamVersion}. " +
            "Write ID {WriteId}",
            eventsArray.Length,
            streamName,
            expectedVersion,
            writeId);

        if (Logger.IsEnabled(LogLevel.Trace))
        {
            foreach (var eventData in convertedEvents)
            {
                Logger.LogTrace(
                    "Event to append {EventId}. EventType {EventType}. WriteId {WriteId}. " +
                    "EventJsonString {EventJsonString}. MetadataJsonString {MetadataJsonString}",
                    eventData.MessageId,
                    eventData.Type,
                    writeId,
                    eventData.JsonData,
                    eventData.JsonMetadata);
            }
        }

        try
        {
            var appendResult =
                await _streamStore.AppendToStream(streamName, expectedVersion, convertedEvents, cancellationToken);

            Logger.LogDebug("Written events to stream. WriteId {WriteId}", writeId);

            Debug.Assert(appendResult.CurrentVersion >= 0);
            return StreamVersion.FromUInt64(Convert.ToUInt64(appendResult.CurrentVersion));
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex, "Unable to save events to stream {StreamName}. Write Id {WriteId}", streamName, writeId);

            throw;
        }
    }
}