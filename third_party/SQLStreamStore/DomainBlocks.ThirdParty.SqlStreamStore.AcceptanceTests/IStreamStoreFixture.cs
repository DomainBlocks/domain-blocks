using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure;

namespace SqlStreamStore
{
    using System;

    public interface IStreamStoreFixture: IDisposable
    {
        IStreamStore Store { get; }

        GetUtcNow GetUtcNow { get; set; }

        long MinPosition { get; set; }

        int MaxSubscriptionCount { get; set; }

        bool DisableDeletionTracking { get; set; }
    }
}