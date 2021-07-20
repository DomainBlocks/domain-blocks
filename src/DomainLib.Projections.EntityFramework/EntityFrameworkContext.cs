using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainLib.Projections.EntityFramework
{
    public class EntityFrameworkContext : IContext
    {
        private readonly DbContext _dbContext;
        //private bool _isProcessingLiveEvents;

        public EntityFrameworkContext(DbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task OnSubscribing()
        {
            return Task.CompletedTask;
        }

        public Task OnCaughtUp()
        {
            return Task.CompletedTask; throw new NotImplementedException();
        }

        public Task OnBeforeHandleEvent()
        {
            return Task.CompletedTask;
        }

        public async Task OnAfterHandleEvent()
        {
            await _dbContext.SaveChangesAsync();
        }
    }
}