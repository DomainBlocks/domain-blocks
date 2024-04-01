using DomainBlocks.V1.Subscriptions;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Persistence;
using Microsoft.EntityFrameworkCore;
using Shopping.ReadModel;

namespace DomainBlocks.V1.Playground;

public class ShoppingCartReadModel : IEventStreamConsumer
{
    private readonly IDbContextFactory<ShoppingCartDbContext> _contextFactory;
    private readonly EventMapper _eventMapper;
    private ShoppingCartDbContext? _dbContext;

    public ShoppingCartReadModel(IDbContextFactory<ShoppingCartDbContext> contextFactory, EventMapper eventMapper)
    {
        _contextFactory = contextFactory;
        _eventMapper = eventMapper;
    }

    public async Task OnInitializing(CancellationToken cancellationToken)
    {
        var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    public async Task OnSubscribing(CancellationToken cancellationToken)
    {
        _dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
    }

    public Task OnSubscriptionDropped(Exception? exception, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnCaughtUp(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnFellBehind(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnEvent(ReadEvent readEvent, CancellationToken cancellationToken)
    {
        var events = _eventMapper.FromReadEvent(readEvent);

        foreach (var e in events)
        {
            Console.WriteLine(e);
        }

        return Task.CompletedTask;
    }

    public Task<GlobalPosition?> OnLoadCheckpointAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<GlobalPosition?>(null);
    }

    public Task OnSaveCheckpointAsync(GlobalPosition position, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}