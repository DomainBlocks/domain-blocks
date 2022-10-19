using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class DbContextProjectionOptionsBuilder<TResource, TDbContext>
    where TDbContext : DbContext where TResource : IDisposable
{
    private readonly DbContextProjectionOptions<TResource, TDbContext> _options;

    public DbContextProjectionOptionsBuilder(DbContextProjectionOptions<TResource, TDbContext> options)
    {
        _options = options;
    }

    public void AddProjection(Action<DbContextProjectionOptionsBuilder<TResource, TDbContext>> optionBuilder)
    {
        optionBuilder(this);
    }

    public void OnInitializing(Func<TDbContext, CancellationToken, Task> onInitializing)
    {
        _options.WithOnInitializing(onInitializing);
    }

    public void When<TEvent>(Func<TEvent, TDbContext, Task> eventHandler)
    {
        _options.WithDefaultEventName<TEvent>();
        _options.WithEventHandler(eventHandler);
    }
}