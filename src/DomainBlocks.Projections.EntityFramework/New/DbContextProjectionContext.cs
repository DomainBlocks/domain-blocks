using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DomainBlocks.Projections.EntityFramework.New;

internal class DbContextProjectionContext<TDbContext> : IProjectionContext where TDbContext : DbContext
{
    private readonly Func<TDbContext, CancellationToken, Task> _onInitializing;
    private readonly Func<IDisposable> _resourceFactory;
    private readonly Func<IDisposable, TDbContext> _dbContextFactory;
    private bool _isCatchingUp;
    private IDisposable _resource;
    private TDbContext _dbContext;
    private IDbContextTransaction _transaction;

    public DbContextProjectionContext(
        Func<TDbContext, CancellationToken, Task> onInitializing,
        Func<IDisposable> resourceFactory,
        Func<IDisposable, TDbContext> dbContextFactory)
    {
        _onInitializing = onInitializing ?? ((_, _) => Task.CompletedTask);
        _resourceFactory = resourceFactory ?? (() => null);
        _dbContextFactory = dbContextFactory;
    }

    public async Task OnInitializing(CancellationToken cancellationToken = default)
    {
        using var resource = _resourceFactory();
        await using var dbContext = _dbContextFactory(resource);
        await _onInitializing(dbContext, cancellationToken);
    }

    public async Task OnCatchingUp(CancellationToken cancellationToken = default)
    {
        _resource = _resourceFactory();
        _dbContext = _dbContextFactory(_resource);
        _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        _isCatchingUp = true;
    }

    public async Task OnCaughtUp(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);
        _resource?.Dispose();
        _isCatchingUp = false;
    }

    public Task OnEventDispatching(CancellationToken cancellationToken = default)
    {
        if (_isCatchingUp) return Task.CompletedTask;
        _resource = _resourceFactory();
        _dbContext = _dbContextFactory(_resource);
        return Task.CompletedTask;
    }

    public async Task OnEventHandled(CancellationToken cancellationToken = default)
    {
        if (_isCatchingUp) return;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _resource?.Dispose();
        _isCatchingUp = true;
    }

    internal RunProjection BindProjectionFunc(Func<object, TDbContext, Task> eventHandler)
    {
        return (e, _) => eventHandler(e, _dbContext);
    }
}