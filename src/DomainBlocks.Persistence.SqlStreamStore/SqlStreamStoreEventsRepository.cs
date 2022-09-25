﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Common;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Logging;
using SqlStreamStore;
using SqlStreamStore.Streams;

namespace DomainBlocks.Persistence.SqlStreamStore
{
    public class SqlStreamStoreEventsRepository : IEventsRepository<string>
    {
        private static readonly ILogger<SqlStreamStoreEventsRepository> Log = Logger.CreateFor<SqlStreamStoreEventsRepository>();
        private readonly IStreamStore _streamStore;
        private readonly IEventSerializer<string> _serializer;
        private readonly int _readPageSize;

        public SqlStreamStoreEventsRepository(
            IStreamStore streamStore, IEventSerializer<string> serializer, int readPageSize = 600)
        {
            _streamStore = streamStore ?? throw new ArgumentNullException(nameof(streamStore));
            _serializer = serializer;
            _readPageSize = readPageSize;
        }

        public async Task<long> SaveEventsAsync(
            string streamName, long expectedStreamVersion, IEnumerable<object> events)
        {
            if (streamName == null) throw new ArgumentNullException(nameof(streamName));
            if (events == null) throw new ArgumentNullException(nameof(events));

            var expectedVersion = MapStreamVersion(expectedStreamVersion);

            NewStreamMessage[] messages;
            try
            {
                messages = events.Select(e => _serializer.ToNewStreamMessage(e)).ToArray();
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Unable to serialize events. Aborting write to stream {StreamName}", streamName);
                throw;
            }

            if (messages.Length == 0)
            {
                Log.LogWarning("No events in batch. Exiting");
                return expectedVersion;
            }

            // Use the ID of the first event in the batch as an identifier for the whole write
            var writeId = messages[0].MessageId;

            Log.LogDebug("Appending {EventCount} events to stream {StreamName}. Expected stream version {StreamVersion}. Write ID {WriteId}",
                         messages.Length, streamName, expectedVersion, writeId);

            if (Log.IsEnabled(LogLevel.Trace))
            {
                foreach (var eventData in messages)
                {
                    Log.LogTrace("Event to append {EventId}. EventType {EventType}. WriteId {WriteId}. " +
                                 "EventJsonString {EventJsonString}. MetadataJsonString {MetadataJsonString}.",
                                 eventData.MessageId, eventData.Type, writeId, eventData.JsonData, eventData.JsonMetadata);
                }
            }

            AppendResult appendResult;

            try
            {
                appendResult = await _streamStore.AppendToStream(streamName, expectedVersion, messages);
                Log.LogDebug("Written events to stream. WriteId {WriteId}", writeId);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Unable to save events to stream {StreamName}. Write Id {WriteId}", streamName, writeId);
                throw;
            }

            return appendResult.CurrentVersion;
        }

        public async IAsyncEnumerable<object> LoadEventsAsync(
            string streamName, long startPosition = 0, Action<IEventPersistenceData<string>> onEventError = null)
        {
            if (streamName == null) throw new ArgumentNullException(nameof(streamName));

            // Get the first page.
            ReadStreamPage page;
            try
            {
                page = await _streamStore.ReadStreamForwards(streamName, (int)startPosition, _readPageSize);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Unable to load events from {StreamName}", streamName);
                throw;
            }

            while (true)
            {
                foreach (var message in page.Messages)
                {
                    object deserializedEvent;

                    try
                    {
                        var jsonData = await message.GetJsonData();
                        deserializedEvent = _serializer.DeserializeEvent(jsonData, message.Type);
                    }
                    catch (EventDeserializeException ex)
                    {
                        if (onEventError == null)
                        {
                            Log.LogWarning(ex, "Error deserializing event and no error handler set up." +
                                               " This may cause data inconsistencies");
                        }
                        else
                        {
                            onEventError(await SqlStreamStoreEventPersistenceData.FromStreamMessage(message));
                            Log.LogInformation(ex, "Error deserializing event. Calling onEventError handler");
                        }

                        continue;
                    }
                    catch (Exception ex)
                    {
                        Log.LogError(ex, "Unable to load events from {StreamName}", streamName);
                        throw;
                    }

                    yield return deserializedEvent;
                }

                if (page.IsEnd)
                {
                    break;
                }

                // Get the next page.
                try
                {
                    page = await page.ReadNext();
                }
                catch (Exception ex)
                {
                    Log.LogError(ex, "Unable to load events from {StreamName}", streamName);
                    throw;
                }
            }
        }

        private static int MapStreamVersion(long expectedStreamVersion)
        {
            if (expectedStreamVersion > int.MaxValue)
            {
                throw new ArgumentException($"SqlStreamStore uses a 32-bit integer for {nameof(expectedStreamVersion)}. " +
                                            $"Your value of {expectedStreamVersion} is too large");
            }

            return expectedStreamVersion switch
            {
                StreamVersion.NewStream => ExpectedVersion.EmptyStream,
                StreamVersion.Any => ExpectedVersion.Any,
                StreamVersion.NoStream => ExpectedVersion.NoStream,
                _ => (int)expectedStreamVersion
            };
        }
    }
}
