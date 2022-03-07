using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.SqlStreamStore.Common.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using SqlStreamStore;
using SqlStreamStore.Streams;

namespace DomainBlocks.Projections.SqlStreamStore.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static ProjectionRegistrationOptionsBuilder<StreamMessageWrapper> UseSqlStreamStorePublishedEvents(
            this ProjectionRegistrationOptionsBuilder builder)
        {
            builder.ServiceCollection.AddPostgresSqlStreamStore(builder.Configuration);
            var builderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure)builder;

            var typedBuilder = builderInfrastructure.TypedAs<StreamMessageWrapper>();
            var typedBuilderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure<StreamMessageWrapper>)typedBuilder;

            typedBuilderInfrastructure.UseEventPublisher(provider =>
            {
                var streamStore = provider.GetRequiredService<IStreamStore>();
                return new SqlStreamStoreEventPublisher(streamStore);
            });

            // Default to JSON deserialization unless overridden
            typedBuilder.UseJsonDeserialization();

            return typedBuilder;
        }

        public static ProjectionRegistrationOptionsBuilder<StreamMessageWrapper> UseJsonDeserialization(
            this ProjectionRegistrationOptionsBuilder<StreamMessageWrapper> builder)
        {
            var builderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure<StreamMessageWrapper>)builder;
            builderInfrastructure.UseEventDeserializer(_ => new StreamMessageJsonDeserializer());
            return builder;
        }
    }
}
