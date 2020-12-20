using DomainLib.Common;
using DomainLib.Serialization;
using EventStore.ClientAPI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DomainLib.Persistence.EventStore
{
    public class EventStoreEventsRepository : IEventsRepository
    {
        private static readonly ILogger<EventStoreEventsRepository> Log = Logger.CreateFor<EventStoreEventsRepository>();
        private readonly IEventStoreConnection _connection;
        private readonly IEventSerializer _serializer;
        private const int MaxWriteBatchSize = 4095;

        public EventStoreEventsRepository(IEventStoreConnection connection, IEventSerializer serializer)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            Log.LogDebug("EventStoreEventsRepository created using {SerializerType} serializer", serializer.GetType().Name);
        }

        public async Task<long> SaveEventsAsync<TEvent>(string streamName, long expectedStreamVersion, IEnumerable<TEvent> events)
        {
            if (streamName == null) throw new ArgumentNullException(nameof(streamName));
            if (events == null) throw new ArgumentNullException(nameof(events));

            EventData[] eventDatas;
            try
            {
                eventDatas = events.Select(e => _serializer.ToEventData(e)).ToArray();
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Unable to serialize events. Aborting write to stream {StreamName}", streamName);
                throw;
            }

            var streamVersion = MapStreamVersionToEventStoreStreamVersion(expectedStreamVersion);
            if (eventDatas.Length == 0)
            {
                Log.LogWarning("No events in batch. Exiting");
                return streamVersion;
            }

            // Use the ID of the first event in the batch as an identifier for the whole write to ES
            var writeId = eventDatas[0].EventId;

            Log.LogDebug("Appending {EventCount} events to stream {StreamName}. Expected stream version {StreamVersion}. Write ID {WriteId}", 
                         eventDatas.Length, streamName, streamVersion, writeId);

            if (Log.IsEnabled(LogLevel.Trace))
            {
                foreach (var eventData in eventDatas)
                {
                    Log.LogTrace("Event to append {EventId}. EventType {EventType}. WriteId {WriteId}. " +
                                     "EventBytes {EventBytes}. MetadataBytes {MetadataBytes}. IsJson {IsJson} ",
                                     eventData.EventId, eventData.Type, writeId, eventData.Data, eventData.Metadata, eventData.IsJson);
                }
            }

            WriteResult writeResult;
            try
            {
                if (eventDatas.Length <= MaxWriteBatchSize)
                {
                    writeResult = await _connection.AppendToStreamAsync(streamName, streamVersion, eventDatas);
                    Log.LogDebug("Written events to stream. WriteId {WriteId}", writeId);
                }
                else
                {
                    using var transaction = await _connection.StartTransactionAsync(streamName, streamVersion);
                    Log.LogDebug("Writing events as transaction. Transaction Id {TransactionId}. Write Id {WriteId}", transaction.TransactionId, writeId);
                    var sliceStartPosition = 0;
                    var eventsToSave = true;

                    while (eventsToSave)
                    {
                        var eventsSlice = eventDatas.Skip(sliceStartPosition).Take(MaxWriteBatchSize);
                        await transaction.WriteAsync(eventsSlice);
                        Log.LogDebug("Slice written to transaction {TransactionId}. Write Id {WriteId}", transaction.TransactionId, writeId);

                        sliceStartPosition += MaxWriteBatchSize;

                        if (sliceStartPosition >= eventDatas.Length)
                        {
                            eventsToSave = false;
                        }
                    }

                    writeResult = await transaction.CommitAsync();
                    Log.LogDebug("Transaction {TransactionId} committed. Write Id {WriteId}", transaction.TransactionId, writeId);
                }
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Unable to save events to stream {StreamName}. Write Id {WriteId}", streamName, writeId);
                throw;
            }

            return writeResult.NextExpectedVersion;
        }

        public async Task<IList<TEvent>> LoadEventsAsync<TEvent>(string streamName, long startPosition = 0, Action<IEventPersistenceData> onEventError = null)
        {
            if (streamName == null) throw new ArgumentNullException(nameof(streamName));
            var sliceStartPosition = startPosition;
            var events = new List<TEvent>();

            StreamEventsSlice eventsSlice;
            do
            {
                try
                {
                    eventsSlice = (await _connection.ReadStreamEventsForwardAsync(streamName, sliceStartPosition, ClientApiConstants.MaxReadSize, false))
                        .ThrowIfNotSuccess(streamName);

                    foreach (var resolvedEvent in eventsSlice.Events)
                    {
                        try
                        {
                            events.Add(_serializer.DeserializeEvent<TEvent>(resolvedEvent.OriginalEvent.Data, resolvedEvent.OriginalEvent.EventType));
                        }
                        catch (EventDeserializeException e)
                        {
                            if (onEventError == null)
                            {
                                Log.LogWarning(e, "Error deserializing event and no error handler set up. This may cause data inconsistencies");
                            }
                            else
                            {
                                onEventError(EventStoreEventPersistenceData.FromRecordedEvent(resolvedEvent.Event));
                                Log.LogInformation(e, "Error deserializing event. Calling onEventError handler");
                            }
                        }
                    }

                    sliceStartPosition = eventsSlice.NextEventNumber;

                }
                catch (Exception ex)
                {
                    Log.LogError(ex, "Unable to load events from {StreamName}", streamName);
                    throw;
                }
            } while (!eventsSlice.IsEndOfStream);

            return events;
        }

        private static long MapStreamVersionToEventStoreStreamVersion(long streamVersion)
        {
            return streamVersion switch
            {
                StreamVersion.Any => ExpectedVersion.Any,
                StreamVersion.NewStream => ExpectedVersion.NoStream,
                _ => streamVersion
            };
        }
    }
}