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
    public string ExplicitIdSelector()
    {
        string id = null;

        for (var i = 0; i < Iterations; i++)
        {
            id = _optionsWithExplicitIdSelector.GetId(_aggregate);
        }

        return id;
    }

    [Benchmark]
    public string DefaultIdSelector()
    {
        string id = null;

        for (var i = 0; i < Iterations; i++)
        {
            id = _optionsWithDefaultIdSelector.GetId(_aggregate);
        }

        return id;
    }

    private class MyAggregate
    {
        public int Id { get; init; }
    }
}