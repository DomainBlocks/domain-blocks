using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DomainBlocks.V1.Abstractions.Subscriptions;
using DomainBlocks.V1.Subscriptions;

namespace DomainBlocks.Core.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, iterationCount: 10)]
public class EventStreamConsumerSessionBenchmarks
{
    private const int EventCount = 1_000_000;
    private EventStreamConsumerSession _session;
    private (TestEvent, SubscriptionPosition)[] _events;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _events = Enumerable
            .Range(0, EventCount)
            .Select(i => (TestEvent.Instance, new SubscriptionPosition((ulong)i)))
            .ToArray();
    }

    [IterationSetup(Target = nameof(Produce))]
    public void ProduceIterationSetup()
    {
        var consumer = new TestEventStreamConsumer();
        var statusSource = new TestEventStreamSubscriptionStatusSource();
        _session = new EventStreamConsumerSession(consumer, statusSource);
        _session.InitializeAsync().Wait();
        _session.StartAsync().Wait();
    }

    [IterationSetup(Target = nameof(Consume))]
    public void ConsumeIterationSetup()
    {
        var consumer = new TestEventStreamConsumer();
        var statusSource = new TestEventStreamSubscriptionStatusSource();
        _session = new EventStreamConsumerSession(consumer, statusSource);
        _session.InitializeAsync().Wait();
        _session.StartAsync().Wait();

        foreach (var (e, pos) in _events)
        {
            _session.NotifyEventReceivedAsync(e, pos).AsTask().Wait();
        }
    }

    [Benchmark]
    public async Task Produce()
    {
        foreach (var (e, pos) in _events)
        {
            await _session.NotifyEventReceivedAsync(e, pos);
        }
    }

    [Benchmark]
    public async Task Consume()
    {
        await _session.FlushAsync();
    }

    private record TestEvent
    {
        public static readonly TestEvent Instance = new();
    }

    private class TestEventStreamConsumer : IEventStreamConsumer, IEventHandler<TestEvent>
    {
        public Task OnEventAsync(EventHandlerContext<TestEvent> context)
        {
            return Task.CompletedTask;
        }
    }

    public class TestEventStreamSubscriptionStatusSource : IEventStreamSubscriptionStatusSource
    {
        public EventStreamSubscriptionStatus SubscriptionStatus => EventStreamSubscriptionStatus.Live;
    }
}