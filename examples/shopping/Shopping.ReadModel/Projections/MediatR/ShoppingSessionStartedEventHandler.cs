using MediatR;
using Shopping.Domain.Events;

namespace Shopping.ReadModel.Projections.MediatR;

public class ShoppingSessionStartedEventHandler : INotificationHandler<EventNotification<ShoppingSessionStarted>>
{
    public Task Handle(EventNotification<ShoppingSessionStarted> notification, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}