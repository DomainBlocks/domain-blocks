using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Subscriptions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

using Queue = SubscriptionQueue<TestSourceEvent, string, string, StreamPosition, LogPosition>;

public class SubscriptionQueueTests
{
    [Test]
    public void OnNext_MatchingEvent_IsQueued()
    {
        var decoder = new TestDecoder();
        using var queue = new Queue(capacity: 2, EventFilter.StreamIds("order-1").Matches);

        queue.OnNext(CreateEvent(0, "order-1", decoder));
        queue.OnNext(CreateEvent(1, "order-2", decoder));
        queue.OnNext(CreateEvent(2, "order-1", decoder));

        queue.Reader.TryRead(out var first).ShouldBeTrue();
        queue.Reader.TryRead(out var second).ShouldBeTrue();
        queue.Reader.TryRead(out _).ShouldBeFalse();
        first.Context.LogPosition.ShouldBe(LogPosition.FromInt64(0));
        second.Context.LogPosition.ShouldBe(LogPosition.FromInt64(2));
        decoder.DecodeCount.ShouldBe(2);
    }

    [Test]
    public void OnNext_QueueFull_StopsForOverflow()
    {
        using var queue = new Queue(capacity: 1, EventFilter.All.Matches);

        queue.OnNext(CreateEvent(0));
        queue.StopReason.ShouldBe(SubscriptionStopReason.None);

        queue.OnNext(CreateEvent(1));

        queue.StopReason.ShouldBe(SubscriptionStopReason.QueueOverflow);
    }

    [Test]
    public async Task OnNext_QueueFull_EndsReaderOnceQueuedEventIsRead()
    {
        using var queue = new Queue(capacity: 1, EventFilter.All.Matches);
        queue.OnNext(CreateEvent(0));

        queue.OnNext(CreateEvent(1));

        (await queue.Reader.WaitToReadAsync()).ShouldBeTrue();
        queue.Reader.TryRead(out var queued).ShouldBeTrue();
        queued.Context.LogPosition.ShouldBe(LogPosition.FromInt64(0));
        (await queue.Reader.WaitToReadAsync()).ShouldBeFalse();
    }

    [Test]
    public void OnNext_QueueFull_LogsOverflowWarningOnce()
    {
        var logger = new RecordingLogger();
        using var queue = new Queue(capacity: 1, EventFilter.All.Matches, logger, "sub-1");

        queue.OnNext(CreateEvent(0));
        logger.Messages.ShouldBeEmpty();

        queue.OnNext(CreateEvent(1));
        queue.OnNext(CreateEvent(2));

        logger.Messages.ShouldBe(
            [(LogLevel.Warning, "[sub: sub-1] queue overflowed (capacity 1); restart pending")]);
    }

    [Test]
    public void OnNext_AfterOverflow_DoesNotDecodeEvents()
    {
        var decoder = new TestDecoder();
        using var queue = new Queue(capacity: 1, EventFilter.All.Matches);
        queue.OnNext(CreateEvent(0, decoder: decoder));
        queue.OnNext(CreateEvent(1, decoder: decoder));
        var decodeCountAtOverflow = decoder.DecodeCount;

        queue.OnNext(CreateEvent(2, decoder: decoder));
        queue.OnNext(CreateEvent(3, decoder: decoder));

        decoder.DecodeCount.ShouldBe(decodeCountAtOverflow);
    }

    [Test]
    public async Task OnNext_UndecodableEvent_FailsReaderAndStopsDecoding()
    {
        var decoder = new TestDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        using var queue = new Queue(capacity: 10, EventFilter.All.Matches);
        queue.OnNext(CreateEvent(0, decoder: decoder));

        decoder.Failure = null;
        queue.OnNext(CreateEvent(1, decoder: decoder));

        decoder.DecodeCount.ShouldBe(1);
        queue.StopReason.ShouldBe(SubscriptionStopReason.None);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await queue.Reader.WaitToReadAsync());

        exception.Message.ShouldBe("Cannot decode.");
    }

    [Test]
    public async Task OnError_TailFailed_FailsReader()
    {
        using var queue = new Queue(capacity: 10, EventFilter.All.Matches);
        var error = new InvalidOperationException("The tail failed.");

        queue.OnError(error);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await queue.Reader.WaitToReadAsync());

        exception.ShouldBeSameAs(error);
    }

    [Test]
    public async Task Dispose_NotYetStopped_EndsReaderOnceQueuedEventIsRead()
    {
        var queue = new Queue(capacity: 10, EventFilter.All.Matches);
        queue.OnNext(CreateEvent(0));

        queue.Dispose();

        queue.StopReason.ShouldBe(SubscriptionStopReason.Disposed);
        queue.Reader.TryRead(out _).ShouldBeTrue();
        (await queue.Reader.WaitToReadAsync()).ShouldBeFalse();
    }

    [Test]
    public void Dispose_AfterOverflow_KeepsOverflowReason()
    {
        var queue = new Queue(capacity: 1, EventFilter.All.Matches);
        queue.OnNext(CreateEvent(0));
        queue.OnNext(CreateEvent(1));

        queue.Dispose();

        queue.StopReason.ShouldBe(SubscriptionStopReason.QueueOverflow);
    }

    private static TestSourceEvent CreateEvent(long position, string streamId = "order-1", TestDecoder? decoder = null)
    {
        return new TestSourceEvent(position, streamId, position, $"event-{position}", decoder ?? new TestDecoder());
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }
}