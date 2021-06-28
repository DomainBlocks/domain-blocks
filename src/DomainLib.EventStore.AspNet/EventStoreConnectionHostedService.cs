using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using Microsoft.Extensions.Hosting;

namespace DomainLib.EventStore.AspNetCore
{
    internal class EventStoreConnectionHostedService : IHostedService
    {
        private readonly IEventStoreConnection _connection;

        public EventStoreConnectionHostedService(IEventStoreConnection connection)
        {
            _connection = connection;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _connection.ConnectAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _connection.Dispose();
            return Task.CompletedTask;
        }
    }
}