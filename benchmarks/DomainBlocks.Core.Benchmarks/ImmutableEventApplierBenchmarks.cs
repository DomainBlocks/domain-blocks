using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using DomainBlocks.Core.Builders;

namespace DomainBlocks.Core.Benchmarks;

[MemoryDiagnoser]
public class ImmutableEventApplierBenchmark
{
    private const int EventCount = 1000;
    private static readonly TestEvents.IEvent[] Events = TestEvents.Generate(EventCount).ToArray();
    private IAggregateType<ImmutableAggregate> _switchEventApplierType;
    private IAggregateType<ImmutableAggregate> _dynamicEventApplierType;
    private IAggregateType<ImmutableAggregate> _autoConfiguredEventAppliersType;
    private IAggregateType<ImmutableAggregate> _autoConfiguredNonMemberEventAppliersType;
    private IAggregateType<ImmutableAggregate> _manuallyConfiguredEventAppliersType;
    private ImmutableAggregate _aggregate;

    [GlobalSetup]
    public void SetUp()
    {
        _switchEventApplierType = new ImmutableAggregateType<ImmutableAggregate, TestEvents.IEvent>()
            .SetEventApplier((agg, e) => agg.Apply(e));

        _dynamicEventApplierType = new ImmutableAggregateType<ImmutableAggregate, TestEvents.IEvent>()
            .SetEventApplier((agg, e) => agg.Apply((dynamic)e));

        var builder = new ImmutableAggregateTypeBuilder<ImmutableAggregate, TestEvents.IEvent>();
        builder.AutoConfigureEvents();
        _autoConfiguredEventAppliersType = builder.Build();

        builder = new ImmutableAggregateTypeBuilder<ImmutableAggregate, TestEvents.IEvent>();
        builder.AutoConfigureEventsFrom(typeof(ImmutableAggregateFunctions));
        _autoConfiguredNonMemberEventAppliersType = builder.Build();

        builder = new ImmutableAggregateTypeBuilder<ImmutableAggregate, TestEvents.IEvent>();
        builder.Event<TestEvents.Event1>().ApplyWith((agg, e) => agg.Apply(e));
        builder.Event<TestEvents.Event2>().ApplyWith((agg, e) => agg.Apply(e));
        builder.Event<TestEvents.Event3>().ApplyWith((agg, e) => agg.Apply(e));
        _manuallyConfiguredEventAppliersType = builder.Build();

        _aggregate = new ImmutableAggregate();
    }

    [Benchmark]
    public int SwitchEventApplier()
    {
        foreach (var @event in Events)
        {
            _switchEventApplierType.InvokeEventApplier(_aggregate, @event);
        }

        return _aggregate.Value;
    }

    [Benchmark]
    public int DynamicEventApplier()
    {
        foreach (var @event in Events)
        {
            _dynamicEventApplierType.InvokeEventApplier(_aggregate, @event);
        }

        return _aggregate.Value;
    }

    [Benchmark]
    public int AutoConfiguredEventAppliers()
    {
        foreach (var @event in Events)
        {
            _autoConfiguredEventAppliersType.InvokeEventApplier(_aggregate, @event);
        }

        return _aggregate.Value;
    }

    [Benchmark]
    public int AutoConfiguredNonMemberEventAppliers()
    {
        foreach (var @event in Events)
        {
            _autoConfiguredNonMemberEventAppliersType.InvokeEventApplier(_aggregate, @event);
        }

        return _aggregate.Value;
    }

    [Benchmark]
    public int ManuallyConfiguredEventAppliers()
    {
        foreach (var @event in Events)
        {
            _manuallyConfiguredEventAppliersType.InvokeEventApplier(_aggregate, @event);
        }

        return _aggregate.Value;
    }

    private class ImmutableAggregate
    {
        public ImmutableAggregate()
        {
        }

        public ImmutableAggregate(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public ImmutableAggregate Apply(TestEvents.IEvent e) => e switch
        {
            TestEvents.Event1 e1 => Apply(e1),
            TestEvents.Event2 e2 => Apply(e2),
            TestEvents.Event3 e3 => Apply(e3),
            _ => throw new ArgumentOutOfRangeException(nameof(e), e, null)
        };

        public ImmutableAggregate Apply(TestEvents.Event1 e) => new(Value + e.Value);
        public ImmutableAggregate Apply(TestEvents.Event2 e) => new(Value + e.Value);
        public ImmutableAggregate Apply(TestEvents.Event3 e) => new(Value + e.Value);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private static class ImmutableAggregateFunctions
    {
        public static ImmutableAggregate Apply(ImmutableAggregate a, TestEvents.Event1 e) => new(a.Value + e.Value);
        public static ImmutableAggregate Apply(ImmutableAggregate a, TestEvents.Event2 e) => new(a.Value + e.Value);
        public static ImmutableAggregate Apply(ImmutableAggregate a, TestEvents.Event3 e) => new(a.Value + e.Value);
    }
}