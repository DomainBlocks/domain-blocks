using System;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class WithDbContextOptionsBuilder<TResource, TDbContext>
    where TResource : IDisposable where TDbContext : DbContext
{
    private readonly EventCatchUpSubscriptionOptionsBuilder _coreBuilder;
    private readonly DbContextProjectionOptions<TResource, TDbContext> _initialOptions;

    public WithDbContextOptionsBuilder(
        EventCatchUpSubscriptionOptionsBuilder coreBuilder,
        DbContextProjectionOptions<TResource, TDbContext> initialOptions)
    {
        _coreBuilder = coreBuilder;
        _initialOptions = initialOptions;
    }

    public void AddProjection(Action<IDbContextProjectionOptionsBuilder<TDbContext>> optionsAction)
    {
        var builder = new DbContextProjectionOptionsBuilder<TResource, TDbContext>(_initialOptions);
        optionsAction(builder);
        _coreBuilder.AddProjectionOptions(builder.Options);
    }
}