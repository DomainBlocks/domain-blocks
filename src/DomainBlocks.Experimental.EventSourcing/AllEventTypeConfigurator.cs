namespace DomainBlocks.Experimental.EventSourcing;

internal static class AllEventTypeConfigurator
{
    public const string DefaultEventApplierMethodName = "Apply";
}

public sealed class AllEventTypeConfigurator<TEventBase> : IEventTypeConfigurator
{
    private string _eventApplierMethodName = AllEventTypeConfigurator.DefaultEventApplierMethodName;
    private bool _isNonPublicAllowed;
    private Func<Type, bool>? _typeFilter;

    public AllEventTypeConfigurator()
    {
    }

    private AllEventTypeConfigurator(AllEventTypeConfigurator<TEventBase> copyFrom)
    {
        _eventApplierMethodName = copyFrom._eventApplierMethodName;
        _isNonPublicAllowed = copyFrom._isNonPublicAllowed;
        _typeFilter = copyFrom._typeFilter;
    }

    public AllEventTypeConfigurator<TEventBase> WithApplierMethodName(string methodName)
    {
        _eventApplierMethodName = methodName;
        return this;
    }

    public AllEventTypeConfigurator<TEventBase> UseNonPublicMethods()
    {
        _isNonPublicAllowed = true;
        return this;
    }

    public AllEventTypeConfigurator<TEventBase> Where(Func<Type, bool> typeFilter)
    {
        _typeFilter = typeFilter;
        return this;
    }

    void IEventTypeConfigurator.Configure<TState>(EventTypeMap<TState>.Builder builder)
    {
        builder.MapAll<TEventBase>(_eventApplierMethodName, _isNonPublicAllowed, _typeFilter);
    }

    IEventTypeConfiguratorBase IDeepCloneable<IEventTypeConfiguratorBase>.Clone()
    {
        return new AllEventTypeConfigurator<TEventBase>(this);
    }
}

public sealed class AllEventTypeConfigurator<TEventBase, TState> : IEventTypeConfigurator<TState>
{
    private EventApplier<TState>? _eventApplier;
    private string? _eventApplierMethodName = AllEventTypeConfigurator.DefaultEventApplierMethodName;
    private bool _isNonPublicAllowed;
    private Func<Type, bool>? _typeFilter;

    public AllEventTypeConfigurator()
    {
    }

    private AllEventTypeConfigurator(AllEventTypeConfigurator<TEventBase, TState> copyFrom)
    {
        _eventApplier = copyFrom._eventApplier;
        _eventApplierMethodName = copyFrom._eventApplierMethodName;
        _isNonPublicAllowed = copyFrom._isNonPublicAllowed;
        _typeFilter = copyFrom._typeFilter;
    }

    public AllEventTypeConfigurator<TEventBase, TState> WithApplier(Func<TState, TEventBase, TState> eventApplier)
    {
        _eventApplier = EventApplier.Create(eventApplier);
        _eventApplierMethodName = null;
        return this;
    }

    public AllEventTypeConfigurator<TEventBase, TState> WithApplier(Action<TState, TEventBase> eventApplier)
    {
        _eventApplier = EventApplier.Create(eventApplier);
        _eventApplierMethodName = null;
        return this;
    }

    public AllEventTypeConfigurator<TEventBase, TState> WithApplierMethodName(string methodName)
    {
        _eventApplierMethodName = methodName;
        _eventApplier = null;
        return this;
    }

    public AllEventTypeConfigurator<TEventBase, TState> UseNonPublicMethods()
    {
        _isNonPublicAllowed = true;
        return this;
    }

    public AllEventTypeConfigurator<TEventBase, TState> Where(Func<Type, bool> typeFilter)
    {
        _typeFilter = typeFilter;
        return this;
    }

    void IEventTypeConfigurator<TState>.Configure(EventTypeMap<TState>.Builder builder)
    {
        if (_eventApplier != null)
        {
            builder.MapAll<TEventBase>(_eventApplier, _typeFilter);
        }
        else if (_eventApplierMethodName != null)
        {
            builder.MapAll<TEventBase>(_eventApplierMethodName, _isNonPublicAllowed, _typeFilter);
        }
    }

    IEventTypeConfiguratorBase IDeepCloneable<IEventTypeConfiguratorBase>.Clone()
    {
        return new AllEventTypeConfigurator<TEventBase, TState>(this);
    }
}