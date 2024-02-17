using System.Runtime.CompilerServices;
using DomainBlocks.Experimental.EventSourcing.Persistence;
using DomainBlocks.Logging;
using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.SqlStreamStore.Experimental;

public sealed class SqlStreamStoreEventRepository : IEventRepository<StreamMessage, NewStreamMessage, int>
{
    private static readonly ILogger<SqlStreamStoreEventRepository> Logger = Log.Create<SqlStreamStoreEventRepository>();
    private readonly IStreamStore _streamStore;
    private readonly int _readPageSize;

    public SqlStreamStoreEventRepository(IStreamStore streamStore, int readPageSize = 600)
    {
        _streamStore = streamStore;
        _readPageSize = readPageSize;
    }

    public int NoStreamVersion => ExpectedVersion.NoStream;

    public async IAsyncEnumerable<StreamMessage> ReadStreamAsync(
        ReadStreamDirection direction,
        string streamId,
        int? fromVersion = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (streamId == null) throw new ArgumentNullException(nameof(streamId));

        // Get the first page.
        ReadStreamPage page;
        try
        {
            if (direction == ReadStreamDirection.Forwards)
            {
                page = await _streamStore.ReadStreamForwards(
                    streamId, fromVersion ?? StreamVersion.Start, _readPageSize, cancellationToken: cancellationToken);
            }
            else
            {
                page = await _streamStore.ReadStreamBackwards(
                    streamId, fromVersion ?? StreamVersion.End, _readPageSize, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to load events from {StreamName}", streamId);
            throw;
        }

        while (true)
        {
            foreach (var message in page.Messages)
            {
                yield return message;
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
                Logger.LogError(ex, "Unable to load events from {StreamName}", streamId);
                throw;
            }
        }
    }

    public async Task<int?> AppendToStreamAsync(
        string streamId,
        IEnumerable<NewStreamMessage> events,
        int? expectedVersion = null,
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

        // Use the ID of the first event in the batch as an identifier for the whole write
        var writeId = eventsArray[0].MessageId;

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
                    "Event to append {EventId}. EventType {EventType}. WriteId {WriteId}. " +
                    "EventJsonString {EventJsonString}. MetadataJsonString {MetadataJsonString}",
                    eventData.MessageId,
                    eventData.Type,
                    writeId,
                    eventData.JsonData,
                    eventData.JsonMetadata);
            }
        }

        AppendResult appendResult;

        try
        {
            appendResult = await _streamStore.AppendToStream(
                streamId, expectedVersion ?? ExpectedVersion.Any, eventsArray, cancellationToken);

            Logger.LogDebug("Written events to stream. WriteId {WriteId}", writeId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to save events to stream {StreamName}. Write Id {WriteId}", streamId, writeId);
            throw;
        }

        return appendResult.CurrentVersion;
    }
}