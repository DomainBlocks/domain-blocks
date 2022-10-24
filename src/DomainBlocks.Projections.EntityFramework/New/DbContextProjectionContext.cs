using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DomainBlocks.Projections.EntityFramework.New;

internal class DbContextProjectionContext<TDbContext> : IProjectionContext where TDbContext : DbContext
{
    private readonly IDbContextProjectionOptions<TDbContext> _options;
    private bool _isCatchingUp;
    private IDisposable _resource;
    private TDbContext _dbContext;
    private IDbContextTransaction _transaction;

    public DbContextProjectionContext(IDbContextProjectionOptions<TDbContext> options)
    {
        _options = options;
    }

    public async Task OnInitializing(CancellationToken cancellationToken = default)
    {
        using var resource = _options.ResourceFactory();
        await using var dbContext = _options.DbContextFactory(resource);
        await _options.OnInitializing(dbContext, cancellationToken);
    }

    public async Task OnCatchingUp(CancellationToken cancellationToken = default)
    {
        if (_isCatchingUp)
        {
            return;
        }

        _isCatchingUp = true;
        _resource = _options.ResourceFactory();
        _dbContext = _options.DbContextFactory(_resource);

        if (_options.CatchUpMode == DbContextProjectionCatchUpMode.UseTransaction)
        {
            _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        await _options.OnCatchingUp(_dbContext, cancellationToken);
    }

    public async Task OnCaughtUp(CancellationToken cancellationToken = default)
    {
        if (!_isCatchingUp)
        {
            return;
        }

        _isCatchingUp = false;
        await _options.OnCaughtUp(_dbContext, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await (_transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask);
        await _options.OnSaved(_dbContext, cancellationToken);

        await Cleanup();
    }

    public Task OnEventDispatching(CancellationToken cancellationToken = default)
    {
        if (_isCatchingUp)
        {
            return Task.CompletedTask;
        }

        _resource = _options.ResourceFactory();
        _dbContext = _options.DbContextFactory(_resource);

        return Task.CompletedTask;
    }

    public async Task OnEventHandled(CancellationToken cancellationToken = default)
    {
        if (_isCatchingUp)
        {
            return;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _options.OnSaved(_dbContext, cancellationToken);

        await Cleanup();
    }

    internal RunProjection BindProjectionFunc(Func<object, TDbContext, Task> eventHandler)
    {
        return (e, _) => eventHandler(e, _dbContext);
    }

    private async Task Cleanup()
    {
        await (_transaction?.DisposeAsync() ?? ValueTask.CompletedTask);
        _transaction = null;

        await (_dbContext?.DisposeAsync() ?? ValueTask.CompletedTask);
        _dbContext = null;

        _resource?.Dispose();
        _resource = null;
    }
}