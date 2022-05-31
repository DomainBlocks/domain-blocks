using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Persistence;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace Shopping.Api.CommandHandlers;

public class AddItemHandler : CommandHandlerBase<AddItemToShoppingCart>
{
    public AddItemHandler(IAggregateRepository<IDomainEvent> repository) : base(repository)
    {
    }

    protected override async Task HandleImpl(AddItemToShoppingCart request, CancellationToken cancellationToken)
    {
        var aggregate = await Repository.LoadAggregate<ShoppingCartState>(request.CartId);

        var cartId = Guid.Parse(request.CartId);
        var itemId = Guid.Parse(request.ItemId);

        var command = new Domain.Commands.AddItemToShoppingCart(cartId, itemId, request.ItemName);
        aggregate.ExecuteCommand(x => ShoppingCartFunctions.Execute(x, command));

        await Repository.SaveAggregate(aggregate);
    }
}