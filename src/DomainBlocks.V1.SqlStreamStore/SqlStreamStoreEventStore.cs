using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using DomainBlocks.Logging;
using DomainBlocks.V1.SqlStreamStore.Extensions;
using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using DomainBlocks.V1.Abstractions;
using Microsoft.Extensions.Logging;
using ErrorMessages = DomainBlocks.V1.Abstractions.Exceptions.ErrorMessages;
using IStreamSubscription = DomainBlocks.V1.Abstractions.IStreamSubscription;
using StreamVersion = DomainBlocks.V1.Abstractions.StreamVersion;
using WrongExpectedVersionException = DomainBlocks.V1.Abstractions.Exceptions.WrongExpectedVersionException;

namespace DomainBlocks.V1.SqlStreamStore;

public sealed class SqlStreamStoreEventStore : IEventStore
{
    private static readonly ILogger<SqlStreamStoreEventStore> Logger = LogProvider.Get<SqlStreamStoreEventStore>();
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
        return ReadStreamInternalAsync(streamName, direction, fromVersion, null, cancellationToken);
    }

    public IAsyncEnumerable<ReadEvent> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamReadOrigin readOrigin = StreamReadOrigin.Default,
        CancellationToken cancellationToken = default)
    {
        return ReadStreamInternalAsync(streamName, direction, null, readOrigin, cancellationToken);
    }

    public IStreamSubscription SubscribeToAll(GlobalPosition? afterPosition = null)
    {
        return new AllStreamSubscription(_streamStore, afterPosition);
    }

    public Task<StreamVersion?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WriteEvent> events,
        StreamVersion expectedVersion,
        CancellationToken cancellationToken = default)
    {
        return AppendToStreamInternalAsync(streamName, events, expectedVersion, null, cancellationToken);
    }

    public Task<StreamVersion?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WriteEvent> events,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default)
    {
        return AppendToStreamInternalAsync(streamName, events, null, expectedState, cancellationToken);
    }

    private async IAsyncEnumerable<ReadEvent> ReadStreamInternalAsync(
        string streamName,
        StreamReadDirection direction,
        StreamVersion? fromVersion,
        StreamReadOrigin? readOrigin,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Get the first page.
        ReadStreamPage page;
        try
        {
            var sqlStreamStoreFromVersion = fromVersion != null
                ? Convert.ToInt32(fromVersion.Value.ToUInt64())
                : readOrigin!.Value.ToSqlStreamStoreStreamVersion(direction);

            if (direction == StreamReadDirection.Forwards)
            {
                page = await _streamStore.ReadStreamForwards(
                    streamName, sqlStreamStoreFromVersion, _readPageSize, cancellationToken: cancellationToken);
            }
            else
            {
                page = await _streamStore.ReadStreamBackwards(
                    streamName, sqlStreamStoreFromVersion, _readPageSize, cancellationToken: cancellationToken);
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
                yield return await message.ToReadEvent(cancellationToken);
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
        StreamVersion? expectedVersion,
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
            expectedVersion?.ToString() ?? expectedState.ToString(),
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
            var sqlStreamStoreExpectedVersion = expectedVersion != null
                ? Convert.ToInt32(expectedVersion.Value.ToUInt64())
                : expectedState!.Value.ToSqlStreamStoreExpectedVersion();

            var appendResult = await _streamStore.AppendToStream(
                streamName, sqlStreamStoreExpectedVersion, convertedEvents, cancellationToken);

            Logger.LogDebug("Written events to stream. WriteId {WriteId}", writeId);

            Debug.Assert(appendResult.CurrentVersion >= 0);
            return StreamVersion.FromInt32(appendResult.CurrentVersion);
        }
        catch (DomainBlocks.ThirdParty.SqlStreamStore.Streams.WrongExpectedVersionException ex)
        {
            if (expectedVersion.HasValue)
                throw new WrongExpectedVersionException(
                    ErrorMessages.WrongExpectedVersion(streamName, expectedVersion.Value), ex);

            throw new WrongExpectedVersionException(
                ErrorMessages.WrongExpectedVersion(streamName, expectedState!.Value), ex);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex, "Unable to save events to stream {StreamName}. Write Id {WriteId}", streamName, writeId);

            throw;
        }
    }
}