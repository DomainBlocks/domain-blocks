using DomainBlocks.Core.Subscriptions.Builders;

namespace DomainBlocks.SqlStreamStore.Subscriptions;

public static class SqlStreamStoreEventStreamSubscriptionBuilderExtensions
{
    public static SqlStreamStoreSubscriptionBuilder UseSqlStreamStore(
        this EventStreamSubscriptionBuilder builder,
        Action<SqlStreamStoreOptionsBuilder> builderAction)
    {
        var streamStoreOptionsBuilder = new SqlStreamStoreOptionsBuilder();
        builderAction(streamStoreOptionsBuilder);
        var streamStoreOptions = streamStoreOptionsBuilder.Options;
        return new SqlStreamStoreSubscriptionBuilder(builder, streamStoreOptions);
    }
}