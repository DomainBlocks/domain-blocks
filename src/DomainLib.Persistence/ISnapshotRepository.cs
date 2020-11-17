using System.Threading.Tasks;

namespace DomainLib.Persistence
{
    public interface ISnapshotRepository
    {
        Task SaveSnapshot<TState>(string snapshotStreamName, long snapshotVersion, TState snapshotState);
        Task<Snapshot<TState>> LoadSnapshot<TState>(string streamName);
    }
}