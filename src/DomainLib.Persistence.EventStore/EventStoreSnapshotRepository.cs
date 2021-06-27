using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DomainLib.Common;
using DomainLib.Serialization;
using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace DomainLib.Persistence.EventStore
{
    public class EventStoreSnapshotRepository : ISnapshotRepository
    {
        private const string SnapshotVersionMetadataKey = "SnapshotVersion";
        private const string SnapshotEventName = "Snapshot";
        private static readonly ILogger<EventStoreSnapshotRepository> Log = Logger.CreateFor<EventStoreSnapshotRepository>();
        private readonly EventStoreClient _client;
        private readonly IEventSerializer<ReadOnlyMemory<byte>> _serializer;

        public EventStoreSnapshotRepository(EventStoreClient client, IEventSerializer<ReadOnlyMemory<byte>> serializer)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async Task SaveSnapshotAsync<TState>(string snapshotKey, long snapshotVersion, TState snapshotState)
        {
            try
            {
                if (snapshotKey == null) throw new ArgumentNullException(nameof(snapshotKey));
                if (snapshotState == null) throw new ArgumentNullException(nameof(snapshotState));

                var snapshotData = new[]
                {
                    _serializer.ToEventData(snapshotState,
                                            SnapshotEventName,
                                            KeyValuePair.Create(SnapshotVersionMetadataKey, snapshotVersion.ToString()))
                };

                await _client.AppendToStreamAsync(snapshotKey, StreamRevision.FromInt64(snapshotVersion), snapshotData);
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
                var readStreamResult = _client.ReadStreamAsync(Direction.Backwards, snapshotKey, StreamPosition.End, 1);
                var readState = await readStreamResult.ReadState;

                if (readState == ReadState.StreamNotFound ||
                    !await readStreamResult.MoveNextAsync())
                {
                    return (false, null);
                }
                
                var resolvedEvent = readStreamResult.Current;

                var snapshotData = _serializer.DeserializeEvent<TState>(resolvedEvent.OriginalEvent.Data,
                                                                        resolvedEvent.OriginalEvent.EventType,
                                                                        typeof(TState));
                var snapshotVersion = long.Parse(_serializer.DeserializeMetadata(resolvedEvent.OriginalEvent.Metadata)[SnapshotVersionMetadataKey]);

                return (true, new Snapshot<TState>(snapshotData, snapshotVersion));

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