using System.Threading.Channels;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamConsumerSession
{
    private readonly IEventStreamConsumer _consumer;
    private readonly Dictionary<Type, Func<EventHandlerContext, Task>> _eventHandlers;
    private int _isInitialized;
    private Channel<SubscriptionMessage>? _messageChannel;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _consumeTask;
    private SubscriptionPosition? _startPosition;

    public EventStreamConsumerSession(IEventStreamConsumer consumer)
    {
        _consumer = consumer;
        _eventHandlers = consumer.GetEventHandlers();
    }

    private Channel<SubscriptionMessage> MessageChannel => _messageChannel ?? throw new InvalidOperationException();

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isInitialized, 1) == 1)
        {
            throw new InvalidOperationException("Session can only be initialized once.");
        }

        return _consumer.OnInitializingAsync(cancellationToken);
    }

    public async Task<SubscriptionPosition?> RestoreAsync(CancellationToken cancellationToken)
    {
        _startPosition = await _consumer.OnRestoreAsync(cancellationToken);
        return _startPosition;
    }

    public void Start()
    {
        _messageChannel = Channel.CreateUnbounded<SubscriptionMessage>();
        _cancellationTokenSource = new CancellationTokenSource();
        _consumeTask = ConsumeAllMessagesAsync(_cancellationTokenSource.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource?.Cancel();
        await (_consumeTask?.WaitAsync(cancellationToken) ?? Task.CompletedTask);

        _cancellationTokenSource?.Dispose();

        _messageChannel = null;
        _cancellationTokenSource = null;
        _consumeTask = null;
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default)
    {
        return (_consumeTask ?? Task.CompletedTask).WaitAsync(cancellationToken);
    }

    public bool CanHandle(Type eventType) => _eventHandlers.ContainsKey(eventType);

    public async ValueTask NotifyEventReceivedAsync(EventHandlerContext context, CancellationToken cancellationToken)
    {
        var message = new ConsumerMessage.EventReceived(context);
        await MessageChannel.Writer.WriteAsync(message, cancellationToken);
    }

    public async ValueTask NotifyCaughtUpAsync(
        SubscriptionMessage.CaughtUp message, CancellationToken cancellationToken)
    {
        await MessageChannel.Writer.WriteAsync(message, cancellationToken);
    }

    public async ValueTask NotifyFellBehindAsync(
        SubscriptionMessage.FellBehind message, CancellationToken cancellationToken)
    {
        await MessageChannel.Writer.WriteAsync(message, cancellationToken);
    }

    public async ValueTask NotifySubscriptionDroppedAsync(
        SubscriptionMessage.SubscriptionDropped message, CancellationToken cancellationToken)
    {
        await MessageChannel.Writer.WriteAsync(message, cancellationToken);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        var message = new ConsumerMessage.FlushRequested();
        await MessageChannel.Writer.WriteAsync(message, cancellationToken);
        await message.WaitForReceiptAsync(cancellationToken);
    }

    private async Task ConsumeAllMessagesAsync(CancellationToken cancellationToken)
    {
        var nextMessageTask = MessageChannel.Reader.ReadAsync(cancellationToken).AsTask();

        while (!cancellationToken.IsCancellationRequested)
        {
            var completedTask = await Task.WhenAny(nextMessageTask);

            if (completedTask == nextMessageTask)
            {
                await HandleMessageAsync(completedTask.Result, cancellationToken);
                nextMessageTask = MessageChannel.Reader.ReadAsync(cancellationToken).AsTask();
            }
        }
    }

    private async Task HandleMessageAsync(SubscriptionMessage message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case ConsumerMessage.EventReceived @event:
                await HandleEventReceivedMessageAsync(@event, cancellationToken);
                break;
            case SubscriptionMessage.CaughtUp caughtUp:
                await HandleCaughtUpMessageAsync(caughtUp, cancellationToken);
                break;
            case SubscriptionMessage.FellBehind fellBehind:
                await HandleFellBehindMessageAsync(fellBehind, cancellationToken);
                break;
            case SubscriptionMessage.SubscriptionDropped subscriptionDropped:
                await HandleSubscriptionDroppedMessageAsync(subscriptionDropped, cancellationToken);
                break;
            case ConsumerMessage.FlushRequested flushRequested:
                flushRequested.NotifyReceived();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(message));
        }
    }

    private async Task HandleEventReceivedMessageAsync(
        ConsumerMessage.EventReceived message, CancellationToken cancellationToken)
    {
        // TODO: How to deal CT here?
        var context = message.EventHandlerContext;

        if (!_eventHandlers.TryGetValue(context.Event.GetType(), out var handler))
        {
            return;
        }

        if (_startPosition?.Value >= context.Position.Value)
        {
            return;
        }

        await handler(context);

        // TODO: Get rid of this
        await _consumer.OnCheckpointAsync(message.EventHandlerContext.Position, cancellationToken);
    }

    private async Task HandleCaughtUpMessageAsync(
        SubscriptionMessage.CaughtUp message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    private async Task HandleFellBehindMessageAsync(
        SubscriptionMessage.FellBehind message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    private async Task HandleSubscriptionDroppedMessageAsync(
        SubscriptionMessage.SubscriptionDropped message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }
}