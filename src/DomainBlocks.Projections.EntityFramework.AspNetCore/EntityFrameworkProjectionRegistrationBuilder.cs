using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.EntityFramework.AspNetCore;

public class EntityFrameworkProjectionRegistrationBuilder<TDbContext> where TDbContext : DbContext
{
    private Action<IServiceProvider, ProjectionRegistryBuilder> _onRegisteringProjections;

    public void WithProjections(Action<ProjectionRegistryBuilder, TDbContext> onRegisteringProjections)
    {
        _onRegisteringProjections = (provider, builder) =>
        {
            var dispatcherScope = provider.CreateScope();
            var dbContext = dispatcherScope.ServiceProvider.GetRequiredService<TDbContext>();
            onRegisteringProjections(builder, dbContext);
        };
    }

    public void WithProjections(Action<ProjectionRegistryBuilder, TDbContext, IServiceProvider> onRegisteringProjections)
    {
        _onRegisteringProjections = (provider, builder) =>
        {
            var dispatcherScope = provider.CreateScope();
            var dbContext = dispatcherScope.ServiceProvider.GetRequiredService<TDbContext>();
            onRegisteringProjections(builder, dbContext, dispatcherScope.ServiceProvider);
        };
    }

    public Action<IServiceProvider, ProjectionRegistryBuilder> Build()
    {
        return (provider, builder) => _onRegisteringProjections(provider, builder);
    }
}