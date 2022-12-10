﻿using BenchmarkDotNet.Attributes;
using DomainBlocks.Core.Builders;

namespace DomainBlocks.Core.Benchmarks;

[MemoryDiagnoser]
public class MutableEventApplierBenchmark
{
    private const int EventCount = 1000;
    private static readonly TestEvents.IEvent[] Events = TestEvents.Generate(EventCount).ToArray();
    private IAggregateOptions<MutableAggregate> _switchEventApplierOptions;
    private IAggregateOptions<MutableAggregate> _dynamicEventApplierOptions;
    private IAggregateOptions<MutableAggregate> _autoConfiguredEventAppliersOptions;
    private IAggregateOptions<MutableAggregate> _manuallyConfiguredEventAppliersOptions;
    private MutableAggregate _aggregate;

    [GlobalSetup]
    public void SetUp()
    {
        _switchEventApplierOptions = new MutableAggregateOptions<MutableAggregate, TestEvents.IEvent>()
            .WithEventApplier((agg, e) => agg.Apply(e));

        _dynamicEventApplierOptions = new MutableAggregateOptions<MutableAggregate, TestEvents.IEvent>()
            .WithEventApplier((agg, e) => agg.Apply((dynamic)e));

        var builder = new MutableAggregateOptionsBuilder<MutableAggregate, TestEvents.IEvent>();
        builder.AutoConfigureEvents();
        _autoConfiguredEventAppliersOptions = builder.Options;

        builder = new MutableAggregateOptionsBuilder<MutableAggregate, TestEvents.IEvent>();
        builder.Event<TestEvents.Event1>().ApplyWith((agg, e) => agg.Apply(e));
        builder.Event<TestEvents.Event2>().ApplyWith((agg, e) => agg.Apply(e));
        builder.Event<TestEvents.Event3>().ApplyWith((agg, e) => agg.Apply(e));
        _manuallyConfiguredEventAppliersOptions = builder.Options;

        _aggregate = new MutableAggregate();
    }

    [Benchmark]
    public int SwitchEventApplier()
    {
        foreach (var @event in Events)
        {
            _switchEventApplierOptions.ApplyEvent(_aggregate, @event);
        }

        return _aggregate.Value;
    }

    [Benchmark]
    public int DynamicEventApplier()
    {
        foreach (var @event in Events)
        {
            _dynamicEventApplierOptions.ApplyEvent(_aggregate, @event);
        }

        return _aggregate.Value;
    }

    [Benchmark]
    public int AutoConfiguredEventAppliers()
    {
        foreach (var @event in Events)
        {
            _autoConfiguredEventAppliersOptions.ApplyEvent(_aggregate, @event);
        }

        return _aggregate.Value;
    }

    [Benchmark]
    public int ManuallyConfiguredEventAppliers()
    {
        foreach (var @event in Events)
        {
            _manuallyConfiguredEventAppliersOptions.ApplyEvent(_aggregate, @event);
        }

        return _aggregate.Value;
    }

    private class MutableAggregate
    {
        public int Value { get; private set; }

        public void Apply(TestEvents.IEvent @event)
        {
            switch (@event)
            {
                case TestEvents.Event1 e1:
                    Apply(e1);
                    return;
                case TestEvents.Event2 e2:
                    Apply(e2);
                    return;
                case TestEvents.Event3 e3:
                    Apply(e3);
                    return;
            }
        }

        public void Apply(TestEvents.Event1 e) => Value += e.Value;
        public void Apply(TestEvents.Event2 e) => Value += e.Value;
        public void Apply(TestEvents.Event3 e) => Value += e.Value;
    }
}