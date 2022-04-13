using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.EntityFramework.AspNetCore
{
    public class EntityFrameworkProjectionRegistrationBuilder<TDbContext> where TDbContext : DbContext
    {
        private Action<IServiceProvider, ProjectionRegistryBuilder> _onRegisteringProjections;

        public void WithProjections(Action<EntityFrameworkProjectionRegistryBuilder<TDbContext>, TDbContext> onRegisteringProjections)
        {
            _onRegisteringProjections = (provider, builder) =>
            {
                var dispatcherScope = provider.CreateScope();
                var dbContext = dispatcherScope.ServiceProvider.GetRequiredService<TDbContext>();
                var efProjectionRegistryBuilder = new EntityFrameworkProjectionRegistryBuilder<TDbContext>(builder, dbContext);
                onRegisteringProjections(efProjectionRegistryBuilder, dbContext);
            };
        }

        public Action<IServiceProvider, ProjectionRegistryBuilder> Build()
        {
            return (provider, builder) => _onRegisteringProjections(provider, builder);
        }
    }

    public class EntityFrameworkProjectionRegistryBuilder<TDbContext>
    {
        private readonly ProjectionRegistryBuilder _inner;
        private readonly TDbContext _dbContext;

        internal EntityFrameworkProjectionRegistryBuilder(ProjectionRegistryBuilder inner, TDbContext dbContext)
        {
            _inner = inner;
            _dbContext = dbContext;
        }

        public EntityFrameworkEventProjectionBuilder<TDbContext, TEvent> Event<TEvent>()
        {
            return new EntityFrameworkEventProjectionBuilder<TDbContext, TEvent>(_inner.Event<TEvent>(), _dbContext);
        }
    }

    public class EntityFrameworkEventProjectionBuilder<TDbContext, TEvent>
    {
        private readonly EventProjectionBuilder<TEvent> _inner;
        private readonly TDbContext _dbContext;

        public EntityFrameworkEventProjectionBuilder(EventProjectionBuilder<TEvent> inner, TDbContext dbContext)
        {
            _inner = inner;
            _dbContext = dbContext;
        }

        public EntityFrameworkEventProjectionBuilder<TDbContext, TEvent> FromName(string name) => _inner.FromName(name);

        public EntityFrameworkEventProjectionBuilder<TDbContext, TEvent> FromNames(params string[] names) => _inner.FromNames(names);
    }
}