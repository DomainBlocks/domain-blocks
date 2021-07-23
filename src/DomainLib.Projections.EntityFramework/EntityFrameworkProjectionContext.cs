using System;
using System.Data;
using System.Threading.Tasks;
using DomainLib.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


namespace DomainLib.Projections.EntityFramework
{
    public class EntityFrameworkProjectionContext : IProjectionContext
    {
        private static readonly ILogger<EntityFrameworkProjectionContext> Log = Logger.CreateFor<EntityFrameworkProjectionContext>();
        private readonly DbContext _dbContext;
        private bool _isProcessingLiveEvents;

        public EntityFrameworkProjectionContext(DbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task OnSubscribing()
        {
            try
            {
                //var connection = _dbContext.Database.GetDbConnection();
                //if (connection.State == ConnectionState.Closed)
                //{
                //    await connection.OpenAsync().ConfigureAwait(false);
                //}

                await _dbContext.Database.EnsureDeletedAsync().ConfigureAwait(false);
                await _dbContext.Database.EnsureCreatedAsync().ConfigureAwait(false);

                _isProcessingLiveEvents = false;
            }
            catch (Exception ex)
            {
                Log.LogCritical(ex, "Exception occurred attempting to handle subscribing to event stream");
                throw;
            }
        }

        public async Task OnCaughtUp()
        {
            _isProcessingLiveEvents = true;
            await _dbContext.SaveChangesAsync().ConfigureAwait(false);
        }

        public Task OnBeforeHandleEvent()
        {
            return Task.CompletedTask;
        }

        public async Task OnAfterHandleEvent()
        {
            if (_isProcessingLiveEvents)
            {
                await _dbContext.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }
}