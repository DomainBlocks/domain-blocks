using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


namespace DomainBlocks.Projections.EntityFramework;

public class EntityFrameworkProjectionContext : IProjectionContext
{
    private static readonly ILogger<EntityFrameworkProjectionContext> Log = Logger.CreateFor<EntityFrameworkProjectionContext>();
    private readonly DbContext _dbContext;
    private bool _isProcessingLiveEvents;

    public EntityFrameworkProjectionContext(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task OnSubscribing(CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _dbContext.Database;
            // await database.EnsureDeletedAsync().ConfigureAwait(false);
            await database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            _isProcessingLiveEvents = false;
        }
        catch (Exception ex)
        {
            Log.LogCritical(ex, "Exception occurred attempting to handle subscribing to event stream");
            throw;
        }
    }

    public async Task OnCaughtUp(CancellationToken cancellationToken = default)
    {
        _isProcessingLiveEvents = true;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task OnBeforeHandleEvent(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task OnAfterHandleEvent(CancellationToken cancellationToken = default)
    {
        if (_isProcessingLiveEvents)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}