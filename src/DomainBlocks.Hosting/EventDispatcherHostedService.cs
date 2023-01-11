using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Core.Projections;
using Microsoft.Extensions.Hosting;

namespace DomainBlocks.Hosting;

public class EventDispatcherHostedService : BackgroundService, IEventDispatcherHostedService
{
    private readonly IEventDispatcher _eventDispatcher;

    public EventDispatcherHostedService(IEventDispatcher eventDispatcher)
    {
        _eventDispatcher = eventDispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _eventDispatcher.StartAsync(stoppingToken);
    }
}