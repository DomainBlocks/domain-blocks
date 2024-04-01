using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Persistence;

namespace DomainBlocks.V1.Subscriptions;

public class ReadModelSubscriptionConsumer<TView> : ICatchUpSubscriptionConsumer
{
    private readonly IReadModelProjection<TView> _projection;
    private readonly EventMapper _eventMapper;
    private TView? _currentView;

    public ReadModelSubscriptionConsumer(IReadModelProjection<TView> projection, EventMapper eventMapper)
    {
        _projection = projection;
        _eventMapper = eventMapper;
    }

    public Task OnInitializing(CancellationToken cancellationToken)
    {
        return _projection.OnInitializingAsync(cancellationToken);
    }

    public async Task OnSubscribing(CancellationToken cancellationToken)
    {
        _currentView = await _projection.GetViewAsync(cancellationToken);
    }

    public Task OnSubscriptionDropped(Exception? exception, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task OnEvent(ReadEvent readEvent, CancellationToken cancellationToken)
    {
        var events = _eventMapper.FromReadEvent(readEvent);
        var view = await GetView(cancellationToken);

        foreach (var e in events)
        {
            view = await _projection.ApplyEventAsync(view, e, cancellationToken);
        }

        _currentView = view;
    }

    public Task OnCaughtUp(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnFellBehind(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task<GlobalPosition?> OnLoadCheckpointAsync(CancellationToken cancellationToken)
    {
        var view = await GetView(cancellationToken);
        return await _projection.OnLoadCheckpointAsync(view, cancellationToken);
    }

    public async Task OnSaveCheckpointAsync(GlobalPosition position, CancellationToken cancellationToken)
    {
        var view = await GetView(cancellationToken);
        await _projection.OnSaveCheckpointAsync(view, position, cancellationToken);
        DisposeView();
    }

    private async ValueTask<TView> GetView(CancellationToken cancellationToken)
    {
        return _currentView ?? await _projection.GetViewAsync(cancellationToken);
    }

    private void DisposeView()
    {
        var disposable = _currentView as IDisposable;
        disposable?.Dispose();
        _currentView = default;
    }
}