using System.Diagnostics;
using System.Threading.Channels;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamConsumerSession
{
    private readonly IEventStreamConsumer _consumer;
    private readonly Dictionary<Type, Func<EventHandlerContext, Task>> _eventHandlers;
    private readonly Channel<SubscriptionMessage> _channel;
    private readonly object _lock = new();
    private CancellationTokenSource _messageLoopCts = new();
    private Task _messageLoopTask = Task.CompletedTask;
    private SubscriptionPosition? _startPosition;

    public EventStreamConsumerSession(IEventStreamConsumer consumer)
    {
        _consumer = consumer;
        _eventHandlers = consumer.GetEventHandlers();
        _channel = Channel.CreateUnbounded<SubscriptionMessage>();
    }

    public EventStreamConsumerSessionStatus Status { get; private set; }
    public bool HasError => Error != null;
    public Exception? Error { get; private set; }
    public SubscriptionMessage? FaultedMessage { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureStatus(EventStreamConsumerSessionStatus.Uninitialized);

        await _consumer.OnInitializingAsync(cancellationToken);

        Status = EventStreamConsumerSessionStatus.Stopped;
    }

    public async Task<SubscriptionPosition?> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureStatus(EventStreamConsumerSessionStatus.Stopped);

        _startPosition = await _consumer.OnRestoreAsync(cancellationToken);

        Status = EventStreamConsumerSessionStatus.Running;
        _messageLoopTask = RunMessageLoopAsync();

        return _startPosition;
    }

    public void Resume()
    {
        lock (_lock)
        {
            EnsureStatus(EventStreamConsumerSessionStatus.Suspended);

            Error = null;
            FaultedMessage = null;

            Debug.Assert(_messageLoopCts.IsCancellationRequested, "Expected message loop to have been cancelled.");
            _messageLoopCts.Dispose();
            _messageLoopCts = new CancellationTokenSource();

            // We set the status before running the message loop to handle the case where everything runs synchronously.
            // In this case, we don't want to transition back to suspended (in the case of an error) before first
            // setting the status back to running.
            Status = EventStreamConsumerSessionStatus.Running;
            _messageLoopTask = RunMessageLoopAsync();
        }
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default)
    {
        return _messageLoopTask.WaitAsync(cancellationToken);
    }

    public bool CanHandleEventType(Type eventType) => _eventHandlers.ContainsKey(eventType);

    public async ValueTask NotifyEventReceivedAsync(
        object @event, SubscriptionPosition position, CancellationToken cancellationToken = default)
    {
        var context = new EventHandlerContext(@event, position, _messageLoopCts.Token);
        var message = new ConsumerMessage.EventReceived(context);
        await _channel.Writer.WriteAsync(message, cancellationToken);
    }

    public async ValueTask NotifyMessageAsync(
        SubscriptionMessage message, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(message, cancellationToken);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        var message = new ConsumerMessage.FlushRequested();
        await _channel.Writer.WriteAsync(message, cancellationToken);
        await message.WaitForReceiptAsync(cancellationToken);
    }

    private async Task RunMessageLoopAsync()
    {
        var nextMessageReadyTask = _channel.Reader.WaitToReadAsync(_messageLoopCts.Token).AsTask();

        while (!_messageLoopCts.Token.IsCancellationRequested)
        {
            var completedTask = await Task.WhenAny(nextMessageReadyTask);

            if (completedTask == nextMessageReadyTask)
            {
                await HandleNextMessageAsync();
                nextMessageReadyTask = _channel.Reader.WaitToReadAsync(_messageLoopCts.Token).AsTask();
            }
        }
    }

    private async Task HandleNextMessageAsync()
    {
        _channel.Reader.TryPeek(out var message);
        Debug.Assert(message != null, "Expected TryPeek to succeed.");

        try
        {
            switch (message)
            {
                case ConsumerMessage.EventReceived @event:
                    await HandleEventReceivedAsync(@event);
                    break;
                case SubscriptionMessage.CaughtUp caughtUp:
                    await HandleCaughtUpAsync(caughtUp);
                    break;
                case SubscriptionMessage.FellBehind fellBehind:
                    await HandleFellBehindAsync(fellBehind);
                    break;
                case SubscriptionMessage.SubscriptionDropped subscriptionDropped:
                    await HandleSubscriptionMessageAsync(subscriptionDropped);
                    break;
                case ConsumerMessage.FlushRequested flushRequested:
                    flushRequested.NotifyReceived();
                    break;
            }

            var success = _channel.Reader.TryRead(out _);
            Debug.Assert(success, "Expected TryRead to succeed.");
        }
        catch (Exception ex)
        {
            SuspendWithError(ex, message);
        }
    }

    private async Task HandleEventReceivedAsync(ConsumerMessage.EventReceived message)
    {
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
        await _consumer.OnCheckpointAsync(message.EventHandlerContext.Position, _messageLoopCts.Token);
    }

    private async Task HandleCaughtUpAsync(SubscriptionMessage.CaughtUp message)
    {
        await Task.CompletedTask;
    }

    private async Task HandleFellBehindAsync(SubscriptionMessage.FellBehind message)
    {
        await Task.CompletedTask;
    }

    private async Task HandleSubscriptionMessageAsync(SubscriptionMessage.SubscriptionDropped message)
    {
        await Task.CompletedTask;
    }

    private void SuspendWithError(Exception exception, SubscriptionMessage faultedMessage)
    {
        lock (_lock)
        {
            EnsureStatus(EventStreamConsumerSessionStatus.Running);

            Error = exception;
            FaultedMessage = faultedMessage;

            _messageLoopCts.Cancel();

            Status = EventStreamConsumerSessionStatus.Suspended;
        }
    }

    private void EnsureStatus(EventStreamConsumerSessionStatus expectedStatus)
    {
        if (Status != expectedStatus)
        {
            throw new InvalidOperationException($"Expected status {expectedStatus}, but got {Status}.");
        }
    }
}