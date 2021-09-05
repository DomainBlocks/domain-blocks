using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Persistence;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace Shopping.Api.CommandHandlers
{
    public class RemoveItemHandler : CommandHandlerBase<RemoveItemFromShoppingCart>
    {
        public RemoveItemHandler(IAggregateRepository<object, IDomainEvent> repository) : base(repository)
        {
        }

        protected override async Task HandleImpl(RemoveItemFromShoppingCart request, CancellationToken cancellationToken)
        {
            var loadedAggregate = await Repository.LoadAggregate(request.CartId, new ShoppingCartState());

            var cartId = Guid.Parse(request.CartId);
            var itemId = Guid.Parse(request.ItemId);

            loadedAggregate.ImmutableDispatchCommand(new Domain.Commands.RemoveItemFromShoppingCart(itemId, cartId));

            await Repository.SaveAggregate(loadedAggregate);
        }
    }
}