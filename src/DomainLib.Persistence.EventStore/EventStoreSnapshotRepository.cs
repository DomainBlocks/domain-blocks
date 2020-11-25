using System;
using DomainLib.Serialization;
using EventStore.ClientAPI;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DomainLib.Persistence.EventStore
{
    public class EventStoreSnapshotRepository : ISnapshotRepository
    {
        private const string SnapshotVersionMetadataKey = "SnapshotVersion";
        private const string SnapshotEventName = "Snapshot";
        private static readonly ILogger<EventStoreSnapshotRepository> Log = Logger.CreateFor<EventStoreSnapshotRepository>();
        private readonly IEventStoreConnection _connection;
        private readonly IEventSerializer _serializer;

        public EventStoreSnapshotRepository(IEventStoreConnection connection, IEventSerializer serializer)
        {
            _connection = connection;
            _serializer = serializer;
        }

        public async Task SaveSnapshotAsync<TState>(string snapshotKey, long snapshotVersion, TState snapshotState)
        {
            try
            {
                if (snapshotKey == null) throw new ArgumentNullException(nameof(snapshotKey));
                if (snapshotState == null) throw new ArgumentNullException(nameof(snapshotState));

                var snapshotData = _serializer.ToEventData(snapshotState, SnapshotEventName, KeyValuePair.Create(SnapshotVersionMetadataKey, snapshotVersion.ToString()));
                await _connection.AppendToStreamAsync(snapshotKey, ExpectedVersion.Any, snapshotData);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Error when attempting to save snapshot. Stream Name {StreamName}. " +
                                 "Snapshot Version {SnapshotVersion}, Snapshot Type {SnapshotType}", 
                             snapshotKey, snapshotVersion, typeof(TState).FullName);
                throw;
            }
        }

        public async Task<(bool isSuccess, Snapshot<TState> snapshot)> TryLoadSnapshotAsync<TState>(string snapshotKey)
        {
            try
            {
                if (snapshotKey == null) throw new ArgumentNullException(nameof(snapshotKey));
                var eventsSlice = (await _connection.ReadStreamEventsBackwardAsync(snapshotKey, StreamPosition.End, 1, false));

                Snapshot<TState> GetSnapshotFromSlice(StreamEventsSlice slice)
                {

                    var @event = slice.Events[0];

                    var snapshotData = _serializer.DeserializeEvent<TState>(@event.OriginalEvent.Data, 
                                                                            @event.OriginalEvent.EventType, 
                                                                            typeof(TState));
                    var snapshotVersion = long.Parse(_serializer.DeserializeMetadata(@event.OriginalEvent.Metadata)[SnapshotVersionMetadataKey]);

                    return new Snapshot<TState>(snapshotData, snapshotVersion);
                }

                return eventsSlice.Status switch
                {
                    SliceReadStatus.Success => (true, GetSnapshotFromSlice(eventsSlice)),
                    SliceReadStatus.StreamNotFound => (false, null),
                    SliceReadStatus.StreamDeleted => throw new StreamDeletedException(snapshotKey),
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Error when attempting to load snapshot. Stream Name: {StreamName}. " +
                                 "Snapshot Type {SnapshotType}",  snapshotKey, typeof(TState).FullName);
                throw;
            }
        }
    }
}