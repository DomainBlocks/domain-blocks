using System.Threading.Channels;
using DomainBlocks.V1.SqlStreamStore.Extensions;
using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.SqlStreamStore;

internal class AllEventStreamSubscription : IEventStreamSubscription
{
    private readonly Channel<SubscriptionMessage> _channel;
    private readonly IAllStreamSubscription _subscription;

    public AllEventStreamSubscription(IReadonlyStreamStore streamStore, GlobalPosition? afterPosition)
    {
        _channel = Channel.CreateUnbounded<SubscriptionMessage>();

        _subscription = streamStore.SubscribeToAll(
            continueAfterPosition: afterPosition?.ToInt64() ?? null,
            streamMessageReceived: async (_, message, ct) =>
            {
                var eventEntry = await message.ToStoredEventEntry(ct);
                var position = new SubscriptionPosition(Convert.ToUInt64(message.Position));
                await _channel.Writer.WriteAsync(new SubscriptionMessage.EventReceived(eventEntry, position), ct);
            },
            subscriptionDropped: (_, _, exception) =>
            {
                var message = new SubscriptionMessage.SubscriptionDropped(exception);

                Task.Run(async () =>
                    {
                        await _channel.Writer.WriteAsync(message);
                        _channel.Writer.Complete(exception);
                    })
                    .Wait();
            },
            hasCaughtUp: hasCaughtUp =>
            {
                SubscriptionMessage message = hasCaughtUp
                    ? SubscriptionMessage.CaughtUp.Instance
                    : SubscriptionMessage.FellBehind.Instance;

                Task.Run(async () => await _channel.Writer.WriteAsync(message)).Wait();
            },
            prefetchJsonData: true);
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    public IAsyncEnumerable<SubscriptionMessage> ConsumeAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}