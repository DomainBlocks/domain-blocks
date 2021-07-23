using System.Threading.Tasks;

namespace DomainLib.Projections
{
    public interface IProjectionContext
    {
        Task OnSubscribing();
        Task OnCaughtUp();
        Task OnBeforeHandleEvent();
        Task OnAfterHandleEvent();
    }
}