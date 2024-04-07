using System.Diagnostics;
using System.Threading.Channels;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamConsumerSession
{
    private readonly IEventStreamConsumer _consumer;
    private readonly Dictionary<Type, Func<EventHandlerContext, Task>> _eventHandlers;
    private volatile EventStreamConsumerSessionStatus _status;
    private Channel<SubscriptionMessage>? _messageChannel;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _messageLoopTask;
    private SubscriptionPosition? _startPosition;

    public EventStreamConsumerSession(IEventStreamConsumer consumer)
    {
        _consumer = consumer;
        _eventHandlers = consumer.GetEventHandlers();
    }

    public EventStreamConsumerSessionStatus Status => _status;
    public bool IsFaulted => _status == EventStreamConsumerSessionStatus.Faulted;
    public Exception? Error { get; private set; }
    public SubscriptionMessage? FaultedMessage { get; private set; }

    private Channel<SubscriptionMessage> MessageChannel => _messageChannel ?? throw new InvalidOperationException();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureStatus(EventStreamConsumerSessionStatus.Uninitialized);
        await _consumer.OnInitializingAsync(cancellationToken);
        _status = EventStreamConsumerSessionStatus.Stopped;
    }

    public async Task<SubscriptionPosition?> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureStatusIsOneOf(EventStreamConsumerSessionStatus.Stopped, EventStreamConsumerSessionStatus.Faulted);
        _status = EventStreamConsumerSessionStatus.Started;

        // Clear any error
        Error = null;
        FaultedMessage = null;

        _startPosition = await _consumer.OnRestoreAsync(cancellationToken);

        _messageChannel ??= Channel.CreateUnbounded<SubscriptionMessage>();
        _cancellationTokenSource = new CancellationTokenSource();
        _messageLoopTask = ConsumeAllMessagesAsync(_cancellationTokenSource.Token);

        return _startPosition;
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default)
    {
        return (_messageLoopTask ?? Task.CompletedTask).WaitAsync(cancellationToken);
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
        var nextMessageReadyTask = MessageChannel.Reader.WaitToReadAsync(cancellationToken).AsTask();

        while (!cancellationToken.IsCancellationRequested)
        {
            var completedTask = await Task.WhenAny(nextMessageReadyTask);

            if (completedTask == nextMessageReadyTask)
            {
                await HandleNextMessageAsync(cancellationToken);
                nextMessageReadyTask = MessageChannel.Reader.WaitToReadAsync(cancellationToken).AsTask();
            }
        }
    }

    private async Task HandleNextMessageAsync(CancellationToken cancellationToken)
    {
        MessageChannel.Reader.TryPeek(out var message);
        Debug.Assert(message != null, "Expected TryPeek to succeed.");

        try
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
            }

            var success = MessageChannel.Reader.TryRead(out _);
            Debug.Assert(success, "Expected TryRead to succeed.");
        }
        catch (Exception ex)
        {
            await SetFaultedAsync(ex, message);
        }
    }

    private async Task HandleEventReceivedMessageAsync(
        ConsumerMessage.EventReceived message, CancellationToken cancellationToken)
    {
        // TODO: How to deal with CT here?
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

    private async Task SetFaultedAsync(Exception exception, SubscriptionMessage faultedMessage)
    {
        EnsureStatus(EventStreamConsumerSessionStatus.Started);
        _status = EventStreamConsumerSessionStatus.Faulted;

        Error = exception;
        FaultedMessage = faultedMessage;

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _messageLoopTask = null;

        await Task.CompletedTask;
    }

    private void EnsureStatus(EventStreamConsumerSessionStatus expectedStatus)
    {
        if (_status != expectedStatus)
        {
            throw new InvalidOperationException($"Expected status {expectedStatus}, but got {_status}.");
        }
    }

    private void EnsureStatusIsOneOf(params EventStreamConsumerSessionStatus[] expectedStatuses)
    {
        if (!expectedStatuses.Contains(_status))
        {
            var expectedStatusesText = string.Join(", ", expectedStatuses);

            throw new InvalidOperationException(
                $"Expected status to be one of [{expectedStatusesText}], but got {_status}.");
        }
    }
}