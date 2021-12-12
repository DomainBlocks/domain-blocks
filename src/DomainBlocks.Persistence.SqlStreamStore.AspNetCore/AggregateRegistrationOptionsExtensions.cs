using DomainBlocks.Persistence.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore.AspNetCore
{
    public static class AggregateRegistrationOptionsExtensions
    {
        public static IAggregateRegistrationOptionsBuilderInfrastructure<string> UseSqlStreamStoreForEvents(
            this IAggregateRegistrationOptionsBuilderInfrastructure<string> builder)
        {
            builder.ServiceCollection.AddPostgresSqlStreamStore(builder.Configuration);

            builder.AddEventsRepository((provider, serializer) =>
            {
                var streamStore = provider.GetRequiredService<IStreamStore>();
                return new SqlStreamStoreEventsRepository(streamStore, serializer);
            });

            return builder;
        }

        public static IAggregateRegistrationOptionsBuilderInfrastructure<string> UseSqlStreamStoreForSnapshots(
            this IAggregateRegistrationOptionsBuilderInfrastructure<string> builder)
        {
            builder.ServiceCollection.AddPostgresSqlStreamStore(builder.Configuration);

            builder.AddSnapshotRepository((provider, serializer) =>
            {
                var streamStore = provider.GetRequiredService<IStreamStore>();
                return new SqlStreamStoreSnapshotRepository(streamStore, serializer);
            });

            return builder;
        }

        public static IAggregateRegistrationOptionsBuilderInfrastructure<string> UseSqlStreamStoreForEventsAndSnapshots(
            this IAggregateRegistrationOptionsBuilderInfrastructure<string> builder)
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
}