using DomainBlocks.Persistence.AspNetCore;
using DomainBlocks.SqlStreamStore.Common.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore.AspNetCore;

public static class AggregateRegistrationOptionsExtensions
{
    public static IAggregateRepositoryOptionsBuilderInfrastructure<string> UseSqlStreamStoreForEvents(
        this IAggregateRepositoryOptionsBuilderInfrastructure<string> builder)
    {
        builder.ServiceCollection.AddPostgresSqlStreamStore(builder.Configuration);

        builder.AddEventsRepository((provider, serializer) =>
        {
            var streamStore = provider.GetRequiredService<IStreamStore>();
            return new SqlStreamStoreEventsRepository(streamStore, serializer);
        });

        return builder;
    }

    public static IAggregateRepositoryOptionsBuilderInfrastructure<string> UseSqlStreamStoreForSnapshots(
        this IAggregateRepositoryOptionsBuilderInfrastructure<string> builder)
    {
        builder.ServiceCollection.AddPostgresSqlStreamStore(builder.Configuration);

        builder.AddSnapshotRepository((provider, serializer) =>
        {
            var streamStore = provider.GetRequiredService<IStreamStore>();
            return new SqlStreamStoreSnapshotRepository(streamStore, serializer);
        });

        return builder;
    }

    public static IAggregateRepositoryOptionsBuilderInfrastructure<string> UseSqlStreamStoreForEventsAndSnapshots(
        this IAggregateRepositoryOptionsBuilderInfrastructure<string> builder)
    {
        builder.ServiceCollection.AddPostgresSqlStreamStore(builder.Configuration);

        builder.AddEventsRepository((provider, serializer) =>
        {
            var streamStore = provider.GetRequiredService<IStreamStore>();
            return new SqlStreamStoreEventsRepository(streamStore, serializer);
        });

        builder.AddSnapshotRepository((provider, serializer) =>
        {
            var streamStore = provider.GetRequiredService<IStreamStore>();
            return new SqlStreamStoreSnapshotRepository(streamStore, serializer);
        });

        return builder;
    }
}