namespace DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure
{
    internal static class ReaderWriterLockSlimExtension
    {
        public static IDisposable UseReadLock(this ReaderWriterLockSlim lockSlim)
        {
            lockSlim.EnterReadLock();
            return new DelegateDisposable(lockSlim.ExitReadLock);
        }

        public static IDisposable UseWriteLock(this ReaderWriterLockSlim lockSlim)
        {
            lockSlim.EnterWriteLock();
            return new DelegateDisposable(lockSlim.ExitWriteLock);
        }
    }
}