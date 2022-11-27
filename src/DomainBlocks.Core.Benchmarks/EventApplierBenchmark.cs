using BenchmarkDotNet.Attributes;
using DomainBlocks.Core.Builders;

namespace DomainBlocks.Core.Benchmarks;

[MemoryDiagnoser]
public class EventApplierBenchmark
{
    private const int EventCount = 1000;
    private IAggregateOptions<MyAggregate> _options1;
    private IAggregateOptions<MyAggregate> _options2;
    private IAggregateOptions<MyAggregate> _options3;
    private IAggregateOptions<MyAggregate> _options4;
    private MyAggregate _aggregate1;
    private MyAggregate _aggregate2;
    private MyAggregate _aggregate3;
    private MyAggregate _aggregate4;
    private IEvent[] _events;

    [GlobalSetup]
    public void SetUp()
    {
        _options1 = new MutableAggregateOptions<MyAggregate, IEvent>()
            .WithEventApplier((agg, e) => agg.Apply(e));

        _options2 = new MutableAggregateOptions<MyAggregate, IEvent>()
            .WithEventApplier((agg, e) => agg.Apply((dynamic)e));

        var builder = new MutableAggregateOptionsBuilder<MyAggregate, IEvent>();
        builder.AutoConfigureEventsFromApplyMethods();
        _options3 = builder.Options;

        builder = new MutableAggregateOptionsBuilder<MyAggregate, IEvent>();
        builder.Event<Event1>().ApplyWith((agg, e) => agg.Apply(e));
        builder.Event<Event2>().ApplyWith((agg, e) => agg.Apply(e));
        builder.Event<Event3>().ApplyWith((agg, e) => agg.Apply(e));
        _options4 = builder.Options;

        _aggregate1 = new MyAggregate();
        _aggregate2 = new MyAggregate();
        _aggregate3 = new MyAggregate();
        _aggregate4 = new MyAggregate();

        _events = Enumerable
            .Range(1, EventCount)
            .Select(x =>
            {
                return (x % 3) switch
                {
                    0 => (IEvent)new Event3(3),
                    1 => new Event1(1),
                    2 => new Event2(2),
                    _ => throw new ArgumentOutOfRangeException()
                };
            })
            .ToArray();
    }

    [Benchmark]
    public int SwitchAllEventsApplier()
    {
        foreach (var @event in _events)
        {
            _options1.ApplyEvent(_aggregate1, @event);
        }

        return _aggregate1.Value;
    }

    [Benchmark]
    public int DynamicAllEventsApplier()
    {
        foreach (var @event in _events)
        {
            _options2.ApplyEvent(_aggregate2, @event);
        }

        return _aggregate2.Value;
    }

    [Benchmark]
    public int AutoConfiguredEventAppliers()
    {
        foreach (var @event in _events)
        {
            _options3.ApplyEvent(_aggregate3, @event);
        }

        return _aggregate3.Value;
    }

    [Benchmark]
    public int ManuallyConfiguredEventAppliers()
    {
        foreach (var @event in _events)
        {
            _options4.ApplyEvent(_aggregate4, @event);
        }

        return _aggregate4.Value;
    }

    private class MyAggregate
    {
        public int Value { get; private set; }

        public void Apply(IEvent @event)
        {
            switch (@event)
            {
                case Event1 e1:
                    Apply(e1);
                    return;
                case Event2 e2:
                    Apply(e2);
                    return;
                case Event3 e3:
                    Apply(e3);
                    return;
            }
        }

        public void Apply(Event1 e)
        {
            Value += e.Value;
        }

        public void Apply(Event2 e)
        {
            Value += e.Value;
        }

        public void Apply(Event3 e)
        {
            Value += e.Value;
        }
    }

    private interface IEvent
    {
    }

    private class Event1 : IEvent
    {
        public Event1(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    private class Event2 : IEvent
    {
        public Event2(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    private class Event3 : IEvent
    {
        public Event3(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }
}