using DomainBlocks.Core.Subscriptions;
using Microsoft.Extensions.Hosting;

namespace DomainBlocks.Hosting;

public class EventStreamSubscriptionHostedService : BackgroundService, IEventStreamSubscriptionHostedService
{
    private readonly IEventStreamSubscription _subscription;

    public EventStreamSubscriptionHostedService(IEventStreamSubscription subscription)
    {
        _subscription = subscription;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _subscription.StartAsync(stoppingToken);
        await _subscription.WaitForCompletedAsync(stoppingToken);
    }
}