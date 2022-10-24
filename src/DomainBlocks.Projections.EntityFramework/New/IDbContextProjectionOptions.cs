using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public interface IDbContextProjectionOptions<TDbContext> : IProjectionOptions where TDbContext : DbContext
{
    public Func<IDisposable> ResourceFactory { get; }
    public Func<IDisposable, TDbContext> DbContextFactory { get; }
    public Func<TDbContext, CancellationToken, Task> OnInitializing { get; }
    public Func<TDbContext, CancellationToken, Task> OnCatchingUp { get; }
    public Func<TDbContext, CancellationToken, Task> OnCaughtUp { get; }
    public Func<TDbContext, CancellationToken, Task> OnEventDispatching { get; }
    public Func<TDbContext, CancellationToken, Task> OnEventHandled { get; }
    public Func<TDbContext, CancellationToken, Task> OnSaved { get; }
    public DbContextProjectionCatchUpMode CatchUpMode { get; }
}