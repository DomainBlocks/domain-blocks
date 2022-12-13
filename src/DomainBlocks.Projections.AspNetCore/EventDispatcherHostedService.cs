using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace DomainBlocks.Projections.AspNetCore;

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