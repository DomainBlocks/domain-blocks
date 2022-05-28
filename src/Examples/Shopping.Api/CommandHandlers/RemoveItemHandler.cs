using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Persistence;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace Shopping.Api.CommandHandlers;

public class RemoveItemHandler : CommandHandlerBase<RemoveItemFromShoppingCart>
{
    public RemoveItemHandler(IAggregateRepository<IDomainEvent> repository) : base(repository)
    {
    }

    protected override async Task HandleImpl(RemoveItemFromShoppingCart request, CancellationToken cancellationToken)
    {
        var loadedAggregate = await Repository.LoadAggregate<ShoppingCartState>(request.CartId);

        var cartId = Guid.Parse(request.CartId);
        var itemId = Guid.Parse(request.ItemId);

        var command = new Domain.Commands.RemoveItemFromShoppingCart(itemId, cartId);
        loadedAggregate.ExecuteCommand(x => ShoppingCartFunctions.Execute(x, command));

        await Repository.SaveAggregate(loadedAggregate);
    }
}