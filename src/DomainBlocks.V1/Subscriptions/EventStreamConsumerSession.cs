using System.Diagnostics;
using System.Threading.Channels;
using DomainBlocks.Logging;
using DomainBlocks.V1.Abstractions.Subscriptions;
using DomainBlocks.V1.Abstractions.Subscriptions.Messages;
using DomainBlocks.V1.Subscriptions.Messages;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamConsumerSession
{
    private static readonly ILogger<EventStreamConsumerSession> Logger = LogProvider.Get<EventStreamConsumerSession>();
    private readonly IEventStreamConsumer _consumer;
    private readonly Dictionary<Type, Func<EventHandlerContext, Task>> _eventHandlers;
    private readonly Channel<ISubscriptionMessage> _channel;
    private readonly CancellationTokenSource _messageLoopCts = new();
    private Task _messageLoopTask = Task.CompletedTask;
    private SubscriptionPosition? _startPosition;
    private EventStreamConsumerSessionStatus _status;

    public EventStreamConsumerSession(IEventStreamConsumer consumer)
    {
        _consumer = consumer;
        _eventHandlers = consumer.GetEventHandlers();

        var channelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        };

        _channel = Channel.CreateUnbounded<ISubscriptionMessage>(channelOptions);
    }

    public string ConsumerName => _consumer.Name;

    public EventStreamConsumerSessionStatus Status
    {
        get => _status;

        private set
        {
            if (_status == value) return;

            var prevStatus = _status;
            _status = value;

            var logLevel = _status == EventStreamConsumerSessionStatus.Suspended
                ? LogLevel.Warning
                : LogLevel.Information;

            Logger.Log(
                logLevel,
                "{ConsumerName} status changed from {PrevStatus} to {Status}.",
                ConsumerName,
                prevStatus,
                _status);
        }
    }

    public bool IsSuspended => Status == EventStreamConsumerSessionStatus.Suspended;
    public bool IsFaulted => Error != null;
    public Exception? Error { get; private set; }
    public ISubscriptionMessage? FaultedMessage { get; private set; }

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
        EnsureStatus(EventStreamConsumerSessionStatus.Suspended);
        Status = EventStreamConsumerSessionStatus.Running;
        _messageLoopTask = RunMessageLoopAsync();
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
        var message = new EventMapped(context);
        await _channel.Writer.WriteAsync(message, cancellationToken);
    }

    public bool TryNotifyEventReceived(object @event, SubscriptionPosition position)
    {
        var context = new EventHandlerContext(@event, position, _messageLoopCts.Token);
        var message = new EventMapped(context);
        return _channel.Writer.TryWrite(message);
    }

    public ValueTask NotifySubscriptionStatusAsync(
        ISubscriptionStatusMessage message, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(message, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var message = new FlushRequested();
        await _channel.Writer.WriteAsync(message, cancellationToken);
        await message.WaitForReceiptAsync(cancellationToken);
    }

    private async Task RunMessageLoopAsync()
    {
        while (!_messageLoopCts.IsCancellationRequested)
        {
            await _channel.Reader.WaitToReadAsync(_messageLoopCts.Token);

            _channel.Reader.TryPeek(out var message);
            Debug.Assert(message != null, "Expected TryPeek to succeed.");

            try
            {
                await HandleMessageAsync(message);

                var success = _channel.Reader.TryRead(out _);
                Debug.Assert(success, "Expected TryRead to succeed.");
            }
            catch (Exception ex)
            {
                Error = ex;
                FaultedMessage = message;
                Status = EventStreamConsumerSessionStatus.Suspended;
                break;
            }
        }

        return;

        async Task HandleMessageAsync(ISubscriptionMessage message)
        {
            switch (message)
            {
                case EventMapped eventMapped:
                    await HandleEventMappedAsync(eventMapped);
                    break;
                case CaughtUp caughtUp:
                    await HandleCaughtUpAsync(caughtUp);
                    break;
                case FellBehind fellBehind:
                    await HandleFellBehindAsync(fellBehind);
                    break;
                case SubscriptionDropped subscriptionDropped:
                    await HandleSubscriptionMessageAsync(subscriptionDropped);
                    break;
                case FlushRequested flushRequested:
                    flushRequested.NotifyReceived();
                    break;
            }
        }
    }

    private async Task HandleEventMappedAsync(EventMapped message)
    {
        var context = message.EventHandlerContext;
        var eventType = context.Event.GetType();

        try
        {
            if (!_eventHandlers.TryGetValue(eventType, out var handler))
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
        catch (Exception ex)
        {
            Logger.LogError(
                ex, "Error handling event. EventType: '{EventType}', Consumer Name: {ConsumerName}",
                eventType,
                ConsumerName);

            throw;
        }
    }

    private async Task HandleCaughtUpAsync(CaughtUp message)
    {
        await Task.CompletedTask;
    }

    private async Task HandleFellBehindAsync(FellBehind message)
    {
        await Task.CompletedTask;
    }

    private async Task HandleSubscriptionMessageAsync(SubscriptionDropped message)
    {
        await Task.CompletedTask;
    }

    private void EnsureStatus(EventStreamConsumerSessionStatus expectedStatus)
    {
        if (Status != expectedStatus)
        {
            throw new InvalidOperationException($"Expected status {expectedStatus}, but got {Status}.");
        }
    }
}