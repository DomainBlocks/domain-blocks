using BenchmarkDotNet.Attributes;

namespace DomainBlocks.Core.Benchmarks;

[MemoryDiagnoser]
public class AggregateIdSelectorBenchmark
{
    private const int Iterations = 1000;
    private AggregateOptionsBase<MyAggregate, object> _optionsWithExplicitIdSelector;
    private AggregateOptionsBase<MyAggregate, object> _optionsWithDefaultIdSelector;
    private MyAggregate _aggregate;

    [GlobalSetup]
    public void SetUp()
    {
        _optionsWithExplicitIdSelector = new ImmutableAggregateOptions<MyAggregate, object>()
            .WithIdSelector(x => x.Id.ToString());

        _optionsWithDefaultIdSelector = new ImmutableAggregateOptions<MyAggregate, object>();

        _aggregate = new MyAggregate { Id = 123 };
    }

    [Benchmark]
    public void ExplicitIdSelector()
    {
        for (var i = 0; i < Iterations; i++)
        {
            _ = _optionsWithExplicitIdSelector.GetId(_aggregate);
        }
    }

    [Benchmark]
    public void DefaultIdSelector()
    {
        for (var i = 0; i < Iterations; i++)
        {
            _ = _optionsWithDefaultIdSelector.GetId(_aggregate);
        }
    }

    private class MyAggregate
    {
        public int Id { get; init; }
    }
}