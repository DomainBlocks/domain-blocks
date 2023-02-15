using BenchmarkDotNet.Attributes;

namespace DomainBlocks.Core.Benchmarks;

[MemoryDiagnoser]
public class EntityIdSelectorBenchmark
{
    private const int Iterations = 1000;
    private EventSourcedEntityTypeBase<MyAggregate> _typeWithExplicitIdSelector;
    private EventSourcedEntityTypeBase<MyAggregate> _typeWithDefaultIdSelector;
    private MyAggregate _aggregate;

    [GlobalSetup]
    public void SetUp()
    {
        _typeWithExplicitIdSelector = new ImmutableAggregateType<MyAggregate, object>()
            .SetIdSelector(x => x.Id.ToString());

        _typeWithDefaultIdSelector = new ImmutableAggregateType<MyAggregate, object>();

        _aggregate = new MyAggregate { Id = 123 };
    }

    [Benchmark]
    public string ExplicitIdSelector()
    {
        string id = null;

        for (var i = 0; i < Iterations; i++)
        {
            id = _typeWithExplicitIdSelector.GetId(_aggregate);
        }

        return id;
    }

    [Benchmark]
    public string DefaultIdSelector()
    {
        string id = null;

        for (var i = 0; i < Iterations; i++)
        {
            id = _typeWithDefaultIdSelector.GetId(_aggregate);
        }

        return id;
    }

    private class MyAggregate
    {
        public int Id { get; init; }
    }
}