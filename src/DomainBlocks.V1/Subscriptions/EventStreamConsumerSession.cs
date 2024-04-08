using System.Diagnostics;
using System.Threading.Channels;
using DomainBlocks.Logging;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions.Messages;
using DomainBlocks.V1.Subscriptions.Messages;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.V1.Subscriptions;

public class EventStreamConsumerSession
{
    private static readonly ILogger<EventStreamConsumerSession> Logger = LogProvider.Get<EventStreamConsumerSession>();
    private readonly IEventStreamConsumer _consumer;
    private readonly Dictionary<Type, Func<EventHandlerContext, Task>> _eventHandlers;
    private readonly Channel<ISubscriptionMessage> _priorityChannel;
    private readonly Channel<ISubscriptionMessage> _eventChannel;
    private readonly CancellationTokenSource _messageLoopCts = new();
    private Task _messageLoopTask = Task.CompletedTask;
    private TaskCompletionSource _suspendedSignal = new();
    private SubscriptionPosition? _startPosition;

    public EventStreamConsumerSession(IEventStreamConsumer consumer)
    {
        _consumer = consumer;
        _eventHandlers = consumer.GetEventHandlers();
        _priorityChannel = Channel.CreateUnbounded<ISubscriptionMessage>();
        _eventChannel = Channel.CreateUnbounded<ISubscriptionMessage>();
    }

    public string ConsumerName => _consumer.Name;
    public EventStreamConsumerSessionStatus Status { get; private set; }
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

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        EnsureStatus(EventStreamConsumerSessionStatus.Suspended);
        await _priorityChannel.Writer.WriteAsync(ResumeRequested.Instance, cancellationToken);
        await FlushAsync(_priorityChannel.Writer, cancellationToken);
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default)
    {
        return _messageLoopTask.WaitAsync(cancellationToken);
    }

    public Task WaitForSuspendedAsync(CancellationToken cancellationToken = default)
    {
        return _suspendedSignal.Task.WaitAsync(cancellationToken);
    }

    public bool CanHandleEventType(Type eventType) => _eventHandlers.ContainsKey(eventType);

    public async ValueTask NotifyEventReceivedAsync(
        object @event, SubscriptionPosition position, CancellationToken cancellationToken = default)
    {
        var context = new EventHandlerContext(@event, position, _messageLoopCts.Token);
        var message = new EventMapped(context);
        await _eventChannel.Writer.WriteAsync(message, cancellationToken);
    }

    public ValueTask NotifySubscriptionStatusAsync(
        ISubscriptionStatusMessage message, CancellationToken cancellationToken = default)
    {
        return _priorityChannel.Writer.WriteAsync(message, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            FlushAsync(_priorityChannel.Writer, cancellationToken),
            FlushAsync(_eventChannel.Writer, cancellationToken));
    }

    private static async Task FlushAsync(
        ChannelWriter<ISubscriptionMessage> channelWriter, CancellationToken cancellationToken)
    {
        var message = new FlushRequested();
        await channelWriter.WriteAsync(message, cancellationToken);
        await message.WaitForReceiptAsync(cancellationToken);
    }

    private async Task RunMessageLoopAsync()
    {
        while (!_messageLoopCts.IsCancellationRequested)
        {
            if (!IsSuspended)
            {
                await RunFullMessageLoopAsync();
            }
            else
            {
                await RunSuspendedMessageLoopAsync();
            }
        }
    }

    private async Task RunFullMessageLoopAsync()
    {
        while (!_messageLoopCts.IsCancellationRequested && !IsSuspended)
        {
            // Process any priority messages first.
            while (_priorityChannel.Reader.TryRead(out var priorityMessage))
            {
                await HandleMessageAsync(priorityMessage);
            }

            // Process priority or event messages, which ever comes first.
            var priorityMessageReadyTask = _priorityChannel.Reader.WaitToReadAsync(_messageLoopCts.Token).AsTask();
            var eventMessageReadyTask = _eventChannel.Reader.WaitToReadAsync(_messageLoopCts.Token).AsTask();

            var completedTask = await Task.WhenAny(priorityMessageReadyTask, eventMessageReadyTask);

            var readyChannel = completedTask == priorityMessageReadyTask ? _priorityChannel : _eventChannel;
            readyChannel.Reader.TryPeek(out var message);
            Debug.Assert(message != null, "Expected TryPeek to succeed.");

            try
            {
                await HandleMessageAsync(message);
                var success = readyChannel.Reader.TryRead(out _);
                Debug.Assert(success, "Expected TryRead to succeed.");
            }
            catch (Exception ex)
            {
                // TODO: Better logging
                Logger.LogError(ex, "Error processing message.");

                Error = ex;
                FaultedMessage = message;
                Status = EventStreamConsumerSessionStatus.Suspended;
                _suspendedSignal.SetResult();
            }
        }
    }

    private async Task RunSuspendedMessageLoopAsync()
    {
        while (!_messageLoopCts.IsCancellationRequested && IsSuspended)
        {
            var message = await _priorityChannel.Reader.ReadAsync(_messageLoopCts.Token);

            switch (message)
            {
                case CaughtUp:
                case FellBehind:
                case SubscriptionDropped:
                    // Ignore for now
                    break;
                case FlushRequested flushRequested:
                    flushRequested.NotifyReceived();
                    break;
                case ResumeRequested:
                    Error = null;
                    FaultedMessage = null;
                    Status = EventStreamConsumerSessionStatus.Running;
                    _suspendedSignal = new TaskCompletionSource();
                    break;
            }
        }
    }

    private async Task HandleMessageAsync(ISubscriptionMessage message)
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

    private async Task HandleEventMappedAsync(EventMapped message)
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

    private void SuspendWithError(Exception exception, ISubscriptionMessage faultedMessage)
    {
        EnsureStatus(EventStreamConsumerSessionStatus.Running);

        Error = exception;
        FaultedMessage = faultedMessage;

        _messageLoopCts.Cancel();

        Status = EventStreamConsumerSessionStatus.Suspended;
    }

    private void EnsureStatus(EventStreamConsumerSessionStatus expectedStatus)
    {
        if (Status != expectedStatus)
        {
            throw new InvalidOperationException($"Expected status {expectedStatus}, but got {Status}.");
        }
    }
}