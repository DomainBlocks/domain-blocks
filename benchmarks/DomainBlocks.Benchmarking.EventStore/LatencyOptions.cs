namespace DomainBlocks.Benchmarking.EventStore;

public sealed record LatencyOptions
{
    /// <summary>Warm-up runs until both this duration and <see cref="MinWarmUpOperations"/> have elapsed.</summary>
    public TimeSpan WarmUp { get; init; } = TimeSpan.FromSeconds(2);

    public int MinWarmUpOperations { get; init; } = 1_000;

    /// <summary>Measurement stops at this many samples or at <see cref="MaxDuration"/>, whichever comes first.</summary>
    public int SampleCount { get; init; } = 10_000;

    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromSeconds(30);
}