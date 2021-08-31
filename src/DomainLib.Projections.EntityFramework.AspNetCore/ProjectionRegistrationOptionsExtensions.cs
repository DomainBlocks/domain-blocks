using System;
using DomainLib.Projections.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Projections.EntityFramework.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static EntityFrameworkProjectionRegistrationBuilder<TDbContext> UseEntityFramework<TDbContext>(
            this IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder) where TDbContext : DbContext
        {
            var entityFrameworkProjectionBuilder = new EntityFrameworkProjectionRegistrationBuilder<TDbContext>(builder);
            builder.AddProjectionRegistrations(entityFrameworkProjectionBuilder.Build());
            return entityFrameworkProjectionBuilder;
        }
    }

    public class EntityFrameworkProjectionRegistrationBuilder<TDbContext> where TDbContext : DbContext
    {
        private readonly IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> _infrastructure;
        private Action<IServiceProvider, ProjectionRegistryBuilder> _onRegisteringProjections;

        public EntityFrameworkProjectionRegistrationBuilder(IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> infrastructure)
        {
            _infrastructure = infrastructure;
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
