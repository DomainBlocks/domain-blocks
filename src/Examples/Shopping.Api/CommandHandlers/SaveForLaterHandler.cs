using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Persistence;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace Shopping.Api.CommandHandlers;

public class SaveForLaterHandler : CommandHandlerBase<SaveItemForLater>
{
    public SaveForLaterHandler(IAggregateRepository<IDomainEvent> repository) : base(repository)
    {
    }

    protected override async Task HandleImpl(SaveItemForLater request, CancellationToken cancellationToken)
    {
        var loadedAggregate = await Repository.LoadAggregate<ShoppingCartState>(request.CartId);

        var cartId = Guid.Parse(request.CartId);
        var itemId = Guid.Parse(request.ItemId);

        var command = new Domain.Commands.SaveItemForLater(cartId, itemId);
        //loadedAggregate.ExecuteCommand(x => ShoppingCartFunctions.Execute(x, command));

        // Snapshot every 25 events
        await Repository.SaveAggregate(loadedAggregate, state => state.EventsLoadedCount >= 25);
    }
}