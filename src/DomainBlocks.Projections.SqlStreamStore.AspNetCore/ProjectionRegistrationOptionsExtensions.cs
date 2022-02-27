using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.SqlStreamStore.Common.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using SqlStreamStore;

namespace DomainBlocks.Projections.SqlStreamStore.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static IProjectionRegistrationOptionsBuilderInfrastructure<SqlStreamStoreRawEvent> UseSqlStreamStorePublishedEvents(
            this IProjectionRegistrationOptionsBuilderInfrastructure<SqlStreamStoreRawEvent> builder)
        {
            builder.ServiceCollection.AddPostgresSqlStreamStore(builder.Configuration);

            builder.AddEventPublisher(provider =>
            {
                var streamStore = provider.GetRequiredService<IStreamStore>();
                return new SqlStreamStoreEventPublisher(streamStore);
            });
            return builder;
        }
    }
}
