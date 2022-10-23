using System;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class WithDbContextOptionsBuilder<TResource, TDbContext>
    where TResource : IDisposable where TDbContext : DbContext
{
    private readonly EventCatchUpSubscriptionOptionsBuilder _rootBuilder;
    private readonly DbContextProjectionOptions<TResource, TDbContext> _initialOptions;

    public WithDbContextOptionsBuilder(
        EventCatchUpSubscriptionOptionsBuilder rootBuilder,
        DbContextProjectionOptions<TResource, TDbContext> initialOptions)
    {
        _rootBuilder = rootBuilder;
        _initialOptions = initialOptions;
    }

    public void AddProjection(Action<IDbContextProjectionOptionsBuilder<TDbContext>> optionsAction)
    {
        var builder = new DbContextProjectionOptionsBuilder<TResource, TDbContext>(_initialOptions);
        optionsAction(builder);
        _rootBuilder.AddProjectionOptions(builder.Options);
    }
}