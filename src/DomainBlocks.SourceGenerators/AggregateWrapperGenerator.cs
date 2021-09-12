using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DomainBlocks.SourceGenerators
{
    [Generator]
    public class AggregateWrapperGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            context.AddSource("ShoppingCartAggregate.cs",
                              SourceText.From(@"using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace Shopping.Api
{
    public class ShoppingCartAggregate
    {
        private readonly CommandDispatcher<object, IDomainEvent> _commandDispatcher;

        public ShoppingCartAggregate(string id,
                                     ShoppingCartState aggregateState,
                                     CommandDispatcher<object, IDomainEvent> commandDispatcher,
                                     long version,
                                     long? snapshotVersion,
                                     long eventsLoadedCount)
        {
            _commandDispatcher = commandDispatcher;
            Id = id;
            AggregateState = aggregateState;
            Version = version;
            SnapshotVersion = snapshotVersion;
            EventsLoadedCount = eventsLoadedCount;
            EventsToPersist = Enumerable.Empty<IDomainEvent>(); ;
        }

        public void AddItemToShoppingCart(AddItemToShoppingCart command)
        {
            ImmutableDispatchCommand(command);
        }

        public void RemoveItemFromShoppingCart(RemoveItemFromShoppingCart command)
        {
            ImmutableDispatchCommand(command);
        }

        public void SaveItemForLater(SaveItemForLater command)
        {
            ImmutableDispatchCommand(command);
        }

        private void ImmutableDispatchCommand(object command)
        {
            var (newState, events) = _commandDispatcher.ImmutableDispatch(AggregateState, command);
            EventsToPersist = EventsToPersist.Concat(events);
            AggregateState = newState;
        }

        public string Id { get; }
        public ShoppingCartState AggregateState { get; private set; }
        public long Version { get; }
        public long? SnapshotVersion { get; }
        public long EventsLoadedCount { get; }
        public IEnumerable<IDomainEvent> EventsToPersist { get; private set; }
    }
}
",
                                              Encoding.UTF8));
        }
    }
}
