namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// What it took to read the log several times over.
/// </summary>
/// <param name="EventsInLog">How many events each pass reads through.</param>
/// <param name="EventsRead">How many events each pass returns.</param>
/// <param name="Passes">How long each pass took.</param>
/// <param name="AllocatedBytes">What the passes allocated between them, on every thread.</param>
/// <param name="Gc">The collections during the passes.</param>
public sealed record ReadThroughputResult(
    int EventsInLog,
    int EventsRead,
    IReadOnlyList<TimeSpan> Passes,
    long AllocatedBytes,
    GcSnapshot Gc)
{
    public TimeSpan MedianPass => Passes.Order().ElementAt(Passes.Count / 2);

    public double EventsPerSecond => EventsInLog / MedianPass.TotalSeconds;

    public double AllocatedBytesPerEvent => (double)AllocatedBytes / Passes.Count / EventsInLog;
}