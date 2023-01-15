namespace DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure
{
    public static class SystemClock
    {
        public static readonly GetUtcNow GetUtcNow = () => DateTime.UtcNow;
    }
}