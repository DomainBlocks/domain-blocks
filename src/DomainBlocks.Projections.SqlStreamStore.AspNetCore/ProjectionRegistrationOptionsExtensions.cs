using System.Text.Json;
using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.SqlStreamStore.Common.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using SqlStreamStore;

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

#nullable enable
        public static ProjectionRegistrationOptionsBuilder<StreamMessageWrapper> UseJsonDeserialization(
            this ProjectionRegistrationOptionsBuilder<StreamMessageWrapper> builder,
            JsonSerializerOptions? serializerOptions = null)
        {
            var builderInfrastructure =
                (IProjectionRegistrationOptionsBuilderInfrastructure<StreamMessageWrapper>)builder;
            builderInfrastructure.UseEventDeserializer(_ => new StreamMessageJsonDeserializer(serializerOptions));
            return builder;
        }
#nullable restore
    }
}
