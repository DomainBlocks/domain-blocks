using System.Threading.Tasks;

namespace DomainBlocks.Projections
{
    public interface IProjectionContext
    {
        Task OnSubscribing();
        Task OnCaughtUp();
        Task OnBeforeHandleEvent();
        Task OnAfterHandleEvent();
    }
}