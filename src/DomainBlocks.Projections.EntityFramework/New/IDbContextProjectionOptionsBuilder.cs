using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public interface IDbContextProjectionOptionsBuilder<out TDbContext> where TDbContext : DbContext
{
    void OnInitializing(Func<TDbContext, CancellationToken, Task> onInitializing);
    void OnCatchingUp(Func<TDbContext, CancellationToken, Task> onCatchingUp);
    void OnCaughtUp(Func<TDbContext, CancellationToken, Task> onCaughtUp);
    void When<TEvent>(Func<TEvent, TDbContext, Task> eventHandler);
    void WithCatchUpMode(DbContextProjectionCatchUpMode catchUpMode);
}