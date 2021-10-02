using System;
using System.Threading.Tasks;

namespace DomainBlocks.Persistence.SqlStreamStore
{
    public class SqlStreamStoreSnapshotRepository : ISnapshotRepository
    {
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