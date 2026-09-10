namespace DomainBlocks.Testing.Integration.Benchmarking;

/// <summary>
/// Garbage collection counts per generation, used to attribute latency outliers to GC pauses.
/// </summary>
public readonly record struct GcSnapshot(int Gen0, int Gen1, int Gen2)
{
    public static GcSnapshot Capture() =>
        new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    public GcSnapshot Since(GcSnapshot earlier) =>
        new(Gen0 - earlier.Gen0, Gen1 - earlier.Gen1, Gen2 - earlier.Gen2);

    public override string ToString() => $"gen0 {Gen0}, gen1 {Gen1}, gen2 {Gen2}";
}
