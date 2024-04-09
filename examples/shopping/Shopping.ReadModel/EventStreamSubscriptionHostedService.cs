using DomainBlocks.V1.Subscriptions;
using Microsoft.Extensions.Hosting;

namespace Shopping.ReadModel;

public class EventStreamSubscriptionHostedService : BackgroundService
{
    private readonly IEnumerable<EventStreamSubscriptionService> _services;

    public EventStreamSubscriptionHostedService(IEnumerable<EventStreamSubscriptionService> services)
    {
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startTasks = _services.Select(x => x.StartAsync(stoppingToken));
        await Task.WhenAll(startTasks);

        var completedTasks = _services.Select(x => x.WaitForCompletedAsync(stoppingToken));
        await Task.WhenAll(completedTasks);
    }
}