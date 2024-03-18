using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.Logging;
using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using Microsoft.Extensions.Logging;
using SqlStreamStoreStreamVersion = DomainBlocks.ThirdParty.SqlStreamStore.Streams.StreamVersion;

namespace DomainBlocks.Experimental.Persistence.SqlStreamStore;

public sealed class SqlStreamStoreEventStore : IEventStore<StreamMessage, NewStreamMessage>
{
    private static readonly ILogger<SqlStreamStoreEventStore> Logger = Log.Create<SqlStreamStoreEventStore>();
    private readonly IStreamStore _streamStore;
    private readonly int _readPageSize;

    public SqlStreamStoreEventStore(IStreamStore streamStore, int readPageSize = 600)
    {
        _streamStore = streamStore;
        _readPageSize = readPageSize;
    }

    public IAsyncEnumerable<StreamMessage> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction,
        StreamVersion fromVersion,
        CancellationToken cancellationToken = default)
    {
        var fromVersionInternal = Convert.ToInt32(fromVersion.ToUInt64());
        return ReadStreamInternalAsync(streamId, direction, fromVersionInternal, cancellationToken);
    }

    public IAsyncEnumerable<StreamMessage> ReadStreamAsync(
        string streamId,
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

        return ReadStreamInternalAsync(streamId, direction, fromVersion, cancellationToken);
    }

    public Task<StreamVersion?> AppendToStreamAsync(
        string streamId,
        IEnumerable<NewStreamMessage> events,
        StreamVersion expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var expectedVersionInternal = Convert.ToInt32(expectedVersion.ToUInt64());
        return AppendToStreamInternalAsync(streamId, events, expectedVersionInternal, cancellationToken);
    }

    public Task<StreamVersion?> AppendToStreamAsync(
        string streamId,
        IEnumerable<NewStreamMessage> events,
        ExpectedStreamState expectedState = ExpectedStreamState.Any,
        CancellationToken cancellationToken = default)
    {
        var expectedVersion = expectedState switch
        {
            ExpectedStreamState.Any => ExpectedVersion.Any,
            ExpectedStreamState.NoStream => ExpectedVersion.NoStream,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedState), expectedState, null)
        };

        return AppendToStreamInternalAsync(streamId, events, expectedVersion, cancellationToken);
    }

    private async IAsyncEnumerable<StreamMessage> ReadStreamInternalAsync(
        string streamId,
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
                    streamId, fromVersion, _readPageSize, cancellationToken: cancellationToken);
            }
            else
            {
                page = await _streamStore.ReadStreamBackwards(
                    streamId, fromVersion, _readPageSize, cancellationToken: cancellationToken);
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

    private async Task<StreamVersion?> AppendToStreamInternalAsync(
        string streamId,
        IEnumerable<NewStreamMessage> events,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
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

        try
        {
            var appendResult =
                await _streamStore.AppendToStream(streamId, expectedVersion, eventsArray, cancellationToken);

            Logger.LogDebug("Written events to stream. WriteId {WriteId}", writeId);

            Debug.Assert(appendResult.CurrentVersion >= 0);
            return StreamVersion.FromUInt64(Convert.ToUInt64(appendResult.CurrentVersion));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to save events to stream {StreamName}. Write Id {WriteId}", streamId, writeId);
            throw;
        }
    }
}