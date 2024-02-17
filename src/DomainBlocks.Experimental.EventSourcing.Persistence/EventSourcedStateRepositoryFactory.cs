namespace DomainBlocks.Experimental.EventSourcing.Persistence;

internal delegate IEventSourcedStateRepository EventSourcedStateRepositoryFactory<TRawData>(
    IEnumerable<IStateEventStreamBinding> bindings,
    StateEventStreamBindingDefaults<TRawData>? defaults);