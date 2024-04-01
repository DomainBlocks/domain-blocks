using System.Threading.Channels;
using DomainBlocks.V1.SqlStreamStore.Extensions;
using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.V1.Abstractions;
using IStreamSubscription = DomainBlocks.V1.Abstractions.IStreamSubscription;

namespace DomainBlocks.V1.SqlStreamStore;

internal class AllStreamSubscription : IStreamSubscription
{
    private readonly Channel<IStreamMessage> _channel;
    private readonly IAllStreamSubscription _subscription;

    public AllStreamSubscription(IReadonlyStreamStore streamStore, GlobalPosition? afterPosition)
    {
        _channel = Channel.CreateUnbounded<IStreamMessage>();

        _subscription = streamStore.SubscribeToAll(
            continueAfterPosition: afterPosition?.ToInt64() ?? null,
            streamMessageReceived: async (_, message, ct) =>
            {
                var readEvent = await message.ToReadEvent(ct);
                await _channel.Writer.WriteAsync(new StreamMessage.Event(readEvent), ct);
            },
            subscriptionDropped: (_, _, exception) =>
            {
                var message = new StreamMessage.SubscriptionDropped(exception);

                var task = Task.Run(async () =>
                {
                    await _channel.Writer.WriteAsync(message);
                    _channel.Writer.Complete(exception);
                });

                task.Wait();
            },
            hasCaughtUp: hasCaughtUp =>
            {
                IStreamMessage message =
                    hasCaughtUp ? StreamMessage.CaughtUp.Instance : StreamMessage.FellBehind.Instance;

                _channel.Writer.TryWrite(message);
            },
            prefetchJsonData: true);
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    public IAsyncEnumerable<IStreamMessage> ConsumeAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}