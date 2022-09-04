using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Persistence;
using Shopping.Domain.Aggregates;

namespace Shopping.Api.CommandHandlers;

public class SaveForLaterHandler : CommandHandlerBase<SaveItemForLater>
{
    public SaveForLaterHandler(IAggregateRepository repository) : base(repository)
    {
    }

    protected override async Task HandleImpl(SaveItemForLater request, CancellationToken cancellationToken)
    {
        var loadedAggregate = await Repository.LoadAsync<ShoppingCartState>(request.CartId);

        var cartId = Guid.Parse(request.CartId);
        var itemId = Guid.Parse(request.ItemId);

        var command = new Domain.Commands.SaveItemForLater(cartId, itemId);
        // TODO (DS): It seems that there is no method on the aggregate for this command, which was previously being
        // hidden behind the command dispatcher. 
        // loadedAggregate.ExecuteCommand(x => ShoppingCartFunctions.Execute(x, command));

        // Snapshot every 25 events
        // TODO (DS): Snapshot strategy can be added to AggregateType.
        await Repository.SaveAsync(loadedAggregate, state => state.EventsLoadedCount >= 25);
    }
}