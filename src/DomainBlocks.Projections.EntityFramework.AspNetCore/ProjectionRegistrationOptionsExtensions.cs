using System;
using DomainBlocks.Projections.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.EntityFramework.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static EntityFrameworkProjectionRegistrationBuilder<TDbContext> UseEntityFramework<TDbContext, TRawData>(
            this IProjectionRegistrationOptionsBuilderInfrastructure<TRawData> builder) where TDbContext : DbContext
        {
            var entityFrameworkProjectionBuilder = new EntityFrameworkProjectionRegistrationBuilder<TDbContext>();
            builder.AddProjectionRegistrations(entityFrameworkProjectionBuilder.Build());
            return entityFrameworkProjectionBuilder;
        }
    }

    public class EntityFrameworkProjectionRegistrationBuilder<TDbContext> where TDbContext : DbContext
    {
        private Action<IServiceProvider, ProjectionRegistryBuilder> _onRegisteringProjections;

        public EntityFrameworkProjectionRegistrationBuilder()
        {
        }

        public void WithProjections(Action<ProjectionRegistryBuilder, TDbContext> onRegisteringProjections)
        {
            _onRegisteringProjections = (provider, builder) =>
            {
                var dispatcherScope = provider.CreateScope();
                var dbContext = dispatcherScope.ServiceProvider.GetRequiredService<TDbContext>();
                onRegisteringProjections(builder, dbContext);
            };
        }

        public Action<IServiceProvider, ProjectionRegistryBuilder> Build()
        {
            return (provider, builder) => _onRegisteringProjections(provider, builder);
        }
    }
}
