using System.Threading.Tasks;

namespace DomainLib.Projections
{
    public interface IContext
    {
        Task OnSubscribing();
        Task OnCaughtUp();
        Task OnBeforeHandleEvent();
        Task OnAfterHandleEvent();
    }
}