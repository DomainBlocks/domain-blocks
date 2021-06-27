using System;
using System.Threading;
using System.Threading.Tasks;
using DomainLib.Aggregates;
using DomainLib.Persistence;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace Shopping.Api.CommandHandlers
{
    public class RemoveItemHandler : CommandHandlerBase<RemoveItemFromShoppingCart>
    {
        public RemoveItemHandler(IAggregateRepository<IDomainEvent> repository,
                                 CommandDispatcher<object, IDomainEvent> commandDispatcher) : base(repository,
            commandDispatcher)
        {
        }

        protected override async Task HandleImpl(RemoveItemFromShoppingCart request, CancellationToken cancellationToken)
        {
            var loadedState = await Repository.LoadAggregate(request.CartId, new ShoppingCartState());

            var cartId = Guid.Parse(request.CartId);
            var itemId = Guid.Parse(request.ItemId);

            var (_, events) = CommandDispatcher.ImmutableDispatch(loadedState.AggregateState,
                                                                  new Domain.Commands.RemoveItemFromShoppingCart(itemId,  cartId));

            await Repository.SaveAggregate<ShoppingCartState>(request.CartId, loadedState.Version, events);
        }
    }
}