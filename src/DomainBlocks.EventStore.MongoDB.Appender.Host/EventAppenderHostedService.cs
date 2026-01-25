namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

public sealed class EventAppenderHostedService(EventAppender eventAppender) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        eventAppender.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return eventAppender.StopAsync(cancellationToken);
    }
}