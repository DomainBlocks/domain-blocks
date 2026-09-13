namespace DomainBlocks.Benchmarking;

public sealed record ThroughputOptions
{
    /// <summary>The number of closed-loop workers, i.e. the maximum number of operations in flight.</summary>
    public required int InFlight { get; init; }

    public TimeSpan WarmUp { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan Measure { get; init; } = TimeSpan.FromSeconds(15);
}