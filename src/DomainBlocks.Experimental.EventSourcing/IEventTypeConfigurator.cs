namespace DomainBlocks.Experimental.EventSourcing;

internal interface IEventTypeConfigurator : IEventTypeConfiguratorBase
{
    void Configure<TState>(EventTypeMap<TState>.Builder builder);
}

internal interface IEventTypeConfigurator<TState> : IEventTypeConfiguratorBase
{
    void Configure(EventTypeMap<TState>.Builder builder);
}