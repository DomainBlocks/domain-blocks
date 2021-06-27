using System;
using System.Threading;
using System.Threading.Tasks;
using DomainLib.Aggregates;
using DomainLib.Persistence;
using EventStore.ClientAPI;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace Shopping.Api.CommandHandlers
{
    public class AddItemHandler : CommandHandlerBase<AddItemToShoppingCart>
    {
        public AddItemHandler(IAggregateRepository<IDomainEvent> repository,
                              CommandDispatcher<object, IDomainEvent> commandDispatcher) : base(repository,
            commandDispatcher)
        {
        }

        protected override async Task HandleImpl(AddItemToShoppingCart request, CancellationToken cancellationToken)
        {
            var initialAggregateState = new ShoppingCartState();

            var cartId = Guid.Parse(request.CartId);
            var itemId = Guid.Parse(request.ItemId);

            var (_, events) = CommandDispatcher.ImmutableDispatch(initialAggregateState,
                                                                  new Domain.Commands.AddItemToShoppingCart(cartId,
                                                                      itemId,
                                                                      request.ItemName));

            await Repository.SaveAggregate<ShoppingCartState>(request.CartId, ExpectedVersion.NoStream, events);
        }
    }
}