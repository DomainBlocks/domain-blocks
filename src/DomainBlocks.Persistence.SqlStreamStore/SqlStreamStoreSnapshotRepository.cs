using System;
using System.Threading.Tasks;
using DomainBlocks.Serialization;
using SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore
{
    public class SqlStreamStoreSnapshotRepository : ISnapshotRepository
    {
        private readonly IStreamStore _streamStore;
        private readonly IEventSerializer<string> _serializer;

        public SqlStreamStoreSnapshotRepository(IStreamStore streamStore, IEventSerializer<string> serializer)
        {
            _streamStore = streamStore;
            _serializer = serializer;
        }

        public Task SaveSnapshotAsync<TState>(string snapshotKey, long snapshotVersion, TState snapshotState)
        {
            throw new NotImplementedException();
        }

        public Task<(bool isSuccess, Snapshot<TState> snapshot)> TryLoadSnapshotAsync<TState>(string snapshotKey)
        {
            throw new NotImplementedException();
        }
    }
}