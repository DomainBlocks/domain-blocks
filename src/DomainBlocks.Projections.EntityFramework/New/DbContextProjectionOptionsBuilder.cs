using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class DbContextProjectionOptionsBuilder<TResource, TDbContext> : IProjectionOptionsProvider
    where TDbContext : DbContext where TResource : IDisposable
{
    private DbContextProjectionOptions<TResource, TDbContext> _options;

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
        _options = _options.WithOnInitializing(onInitializing);
    }

    public void When<TEvent>(Func<TEvent, TDbContext, Task> eventHandler)
    {
        _options = _options.WithEventHandler(eventHandler);
    }

    IEnumerable<IProjectionOptions> IProjectionOptionsProvider.GetProjectionOptions()
    {
        yield return _options;
    }
}