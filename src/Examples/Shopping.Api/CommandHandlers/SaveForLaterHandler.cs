using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Persistence;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace Shopping.Api.CommandHandlers
{
    public class SaveForLaterHandler : CommandHandlerBase<SaveItemForLater>
    {
        public SaveForLaterHandler(IAggregateRepository<object, IDomainEvent> repository) : base(repository)
        {
        }

        protected override async Task HandleImpl(SaveItemForLater request, CancellationToken cancellationToken)
        {
            var loadedAggregate = await Repository.LoadAggregate(request.CartId, new ShoppingCartState());

            var cartId = Guid.Parse(request.CartId);
            var itemId = Guid.Parse(request.ItemId);

            loadedAggregate.ImmutableDispatchCommand(new Domain.Commands.SaveItemForLater(cartId, itemId));

            // Snapshot every 25 events
            await Repository.SaveAggregate(loadedAggregate, state => state.EventsLoadedCount >= 25);
        }
    }
}