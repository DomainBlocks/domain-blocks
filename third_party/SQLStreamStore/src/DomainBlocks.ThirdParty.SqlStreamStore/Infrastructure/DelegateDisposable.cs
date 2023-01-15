namespace DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure
{
    internal class DelegateDisposable : IDisposable
    {
        private readonly Action _action;

        public DelegateDisposable(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            _action();
        }
    }
}