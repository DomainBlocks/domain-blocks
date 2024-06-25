using System.Threading.Channels;
using DomainBlocks.V1.SqlStreamStore.Extensions;
using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions;
using DomainBlocks.V1.Abstractions.Subscriptions.Messages;

namespace DomainBlocks.V1.SqlStreamStore;

internal class AllEventStreamSubscription : IEventStreamSubscription
{
    private readonly Channel<ISubscriptionMessage> _channel;
    private readonly IAllStreamSubscription _subscription;

    public AllEventStreamSubscription(IReadonlyStreamStore streamStore, GlobalPosition? afterPosition)
    {
        _channel = Channel.CreateUnbounded<ISubscriptionMessage>();

        _subscription = streamStore.SubscribeToAll(
            continueAfterPosition: afterPosition?.ToInt64() ?? null,
            streamMessageReceived: async (_, message, ct) =>
            {
                var eventRecord = await message.ToStoredEventRecord(ct);
                var position = new SubscriptionPosition(Convert.ToUInt64(message.Position));
                await _channel.Writer.WriteAsync(new EventReceived(eventRecord, position), ct);
            },
            subscriptionDropped: (_, _, exception) =>
            {
                var message = new SubscriptionDropped(exception);

                Task.Run(async () =>
                    {
                        await _channel.Writer.WriteAsync(message);
                        _channel.Writer.Complete(exception);
                    })
                    .Wait();
            },
            hasCaughtUp: hasCaughtUp =>
            {
                ISubscriptionMessage message = hasCaughtUp ? CaughtUp.Instance : FellBehind.Instance;
                Task.Run(async () => await _channel.Writer.WriteAsync(message)).Wait();
            },
            prefetchJsonData: true);
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    public IAsyncEnumerable<ISubscriptionMessage> ConsumeAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}