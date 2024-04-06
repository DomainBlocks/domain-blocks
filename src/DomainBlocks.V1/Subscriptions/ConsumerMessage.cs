using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public abstract class ConsumerMessage : SubscriptionMessage
{
    public new class EventReceived : ConsumerMessage
    {
        public EventReceived(EventHandlerContext eventHandlerContext)
        {
            EventHandlerContext = eventHandlerContext;
        }

        public EventHandlerContext EventHandlerContext { get; }
    }

    public class FlushRequested : ConsumerMessage
    {
        private readonly TaskCompletionSource _taskCompletionSource = new();

        public Task WaitForReceiptAsync(CancellationToken cancellationToken = default)
        {
            return _taskCompletionSource.Task.WaitAsync(cancellationToken);
        }

        public void NotifyReceived()
        {
            _taskCompletionSource.SetResult();
        }
    }
}