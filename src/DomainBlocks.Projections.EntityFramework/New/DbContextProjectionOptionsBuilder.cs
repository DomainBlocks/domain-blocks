using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class DbContextProjectionOptionsBuilder<TResource, TDbContext> :
    IDbContextProjectionOptionsBuilder<TDbContext>
    where TDbContext : DbContext
    where TResource : IDisposable
{
    public DbContextProjectionOptionsBuilder(DbContextProjectionOptions<TResource, TDbContext> options)
    {
        Options = options;
    }
    
    public DbContextProjectionOptions<TResource, TDbContext> Options { get; private set; }

    public void OnInitializing(Func<TDbContext, CancellationToken, Task> onInitializing)
    {
        Options = Options.WithOnInitializing(onInitializing);
    }

    public void OnCatchingUp(Func<TDbContext, CancellationToken, Task> onCatchingUp)
    {
        Options = Options.WithOnCatchingUp(onCatchingUp);
    }

    public void OnCaughtUp(Func<TDbContext, CancellationToken, Task> onCaughtUp)
    {
        Options = Options.WithOnCaughtUp(onCaughtUp);
    }

    public void When<TEvent>(Func<TEvent, TDbContext, Task> eventHandler)
    {
        Options = Options.WithEventHandler(eventHandler);
    }

    public void When<TEvent>(Action<TEvent, TDbContext> eventHandler)
    {
        Options = Options.WithEventHandler<TEvent>((e, dbContext) =>
        {
            eventHandler(e, dbContext);
            return Task.CompletedTask;
        });
    }

    public void OnSaved(Func<TDbContext, CancellationToken, Task> onSaved)
    {
        Options = Options.WithOnSaved(onSaved);
    }

    public void WithCatchUpMode(DbContextProjectionCatchUpMode catchUpMode)
    {
        Options = Options.WithCatchUpMode(catchUpMode);
    }
}