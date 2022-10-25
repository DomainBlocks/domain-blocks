using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

internal class ServiceProjectionContext<TService> : IProjectionContext
{
    private readonly IServiceProjectionOptions<TService> _options;
    private bool _isCatchingUp;
    private IDisposable _resource;
    private TService _service;

    public ServiceProjectionContext(IServiceProjectionOptions<TService> options)
    {
        _options = options;
    }

    public async Task OnInitializing(CancellationToken cancellationToken = default)
    {
        using var resource = _options.ResourceFactory();
        var service = _options.ServiceFactory(resource);
        await _options.OnInitializing(service, cancellationToken);
    }

    public async Task OnCatchingUp(CancellationToken cancellationToken = default)
    {
        if (_isCatchingUp)
        {
            return;
        }

        _isCatchingUp = true;
        _resource = _options.ResourceFactory();
        _service = _options.ServiceFactory(_resource);

        await _options.OnCatchingUp(_service, cancellationToken);
    }

    public async Task OnCaughtUp(CancellationToken cancellationToken = default)
    {
        if (!_isCatchingUp)
        {
            return;
        }

        _isCatchingUp = false;
        await _options.OnCaughtUp(_service, cancellationToken);

        Cleanup();
    }

    public async Task OnEventDispatching(CancellationToken cancellationToken = default)
    {
        if (_isCatchingUp)
        {
            return;
        }

        _resource = _options.ResourceFactory();
        _service = _options.ServiceFactory(_resource);

        await _options.OnEventDispatching(_service, cancellationToken);
    }

    public async Task OnEventHandled(CancellationToken cancellationToken = default)
    {
        if (_isCatchingUp)
        {
            return;
        }

        await _options.OnEventHandled(_service, cancellationToken);

        Cleanup();
    }

    internal RunProjection BindProjectionFunc(Func<object, TService, Task> eventHandler)
    {
        return (e, _) => eventHandler(e, _service);
    }

    private void Cleanup()
    {
        _service = default;
        _resource?.Dispose();
        _resource = null;
    }
}