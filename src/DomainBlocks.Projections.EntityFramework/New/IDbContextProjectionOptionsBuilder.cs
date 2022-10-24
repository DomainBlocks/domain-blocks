using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public interface IDbContextProjectionOptionsBuilder<out TDbContext> where TDbContext : DbContext
{
    public void OnInitializing(Func<TDbContext, CancellationToken, Task> onInitializing);
    public void OnCatchingUp(Func<TDbContext, CancellationToken, Task> onCatchingUp);
    public void OnCaughtUp(Func<TDbContext, CancellationToken, Task> onCaughtUp);
    public void When<TEvent>(Func<TEvent, TDbContext, Task> eventHandler);
    public void When<TEvent>(Action<TEvent, TDbContext> eventHandler);
    public void OnSaved(Func<TDbContext, CancellationToken, Task> onSaved);
    public void WithCatchUpMode(DbContextProjectionCatchUpMode catchUpMode);
}