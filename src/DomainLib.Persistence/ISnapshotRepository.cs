using System.Threading.Tasks;

namespace DomainLib.Persistence
{
    public interface ISnapshotRepository
    {
        Task SaveSnapshotAsync<TState>(string snapshotKey, long snapshotVersion, TState snapshotState);
        Task<(bool isSuccess, Snapshot<TState> snapshot)> TryLoadSnapshotAsync<TState>(string snapshotKey);
    }
}