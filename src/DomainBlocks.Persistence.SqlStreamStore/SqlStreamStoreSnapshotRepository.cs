using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DomainBlocks.Common;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Logging;
using SqlStreamStore;
using SqlStreamStore.Streams;

namespace DomainBlocks.Persistence.SqlStreamStore
{
    public class SqlStreamStoreSnapshotRepository : ISnapshotRepository
    {
        private const string SnapshotVersionMetadataKey = "SnapshotVersion";
        private const string SnapshotEventName = "Snapshot";
        private static readonly ILogger<SqlStreamStoreSnapshotRepository> Log = Logger.CreateFor<SqlStreamStoreSnapshotRepository>();
        private readonly IStreamStore _streamStore;
        private readonly IEventSerializer<string> _serializer;

        public SqlStreamStoreSnapshotRepository(IStreamStore streamStore, IEventSerializer<string> serializer)
        {
            _streamStore = streamStore;
            _serializer = serializer;
        }

        public async Task SaveSnapshotAsync<TState>(string snapshotKey, long snapshotVersion, TState snapshotState)
        {
            if (snapshotKey == null) throw new ArgumentNullException(nameof(snapshotKey));
            if (snapshotState == null) throw new ArgumentNullException(nameof(snapshotState));

            try
            {
                var snapshotData = new[]
                {
                    _serializer.ToNewStreamMessage(snapshotState,
                                                   SnapshotEventName,
                                                   KeyValuePair.Create(SnapshotVersionMetadataKey, snapshotVersion.ToString()))
                };

                await _streamStore.AppendToStream(snapshotKey, ExpectedVersion.Any, snapshotData);
            }
            catch (Exception ex)
            {
                Log.LogError(ex,
                             "Error when attempting to save snapshot. Stream Name {StreamName}. " +
                             "Snapshot Version {SnapshotVersion}, Snapshot Type {SnapshotType}",
                             snapshotKey,
                             snapshotVersion,
                             typeof(TState).FullName);
                throw;
            }
        }

        public async Task<(bool isSuccess, Snapshot<TState> snapshot)> TryLoadSnapshotAsync<TState>(string snapshotKey)
        {
            try
            {
                if (snapshotKey == null) throw new ArgumentNullException(nameof(snapshotKey));
                var readStreamPage = await _streamStore.ReadStreamBackwards(snapshotKey, global::SqlStreamStore.Streams.StreamVersion.End,  1);
                var messages = readStreamPage.Messages;

                if (messages is not { Length: 1 })
                {
                    return (false, null);
                }

                var snapshotMessage = messages[0];
                
                var snapshotState = (TState)_serializer.DeserializeEvent(
                    await snapshotMessage.GetJsonData(), snapshotMessage.Type, typeof(TState));

                var snapshotVersion = long.Parse(_serializer.DeserializeMetadata(snapshotMessage.JsonMetadata)[SnapshotVersionMetadataKey]);
                return (true, new Snapshot<TState>(snapshotState, snapshotVersion));
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Error when attempting to load snapshot. Stream Name: {StreamName}. " +
                                 "Snapshot Type {SnapshotType}", snapshotKey, typeof(TState).FullName);
                throw;
            }
        }
    }
}