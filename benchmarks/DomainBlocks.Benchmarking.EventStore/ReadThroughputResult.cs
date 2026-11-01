namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// The result of reading the log from start to end several times.
/// </summary>
/// <param name="EventsInLog">The number of events in the log.</param>
/// <param name="EventsRead">The number of events that each pass returns.</param>
/// <param name="Passes">The duration of each measured pass.</param>
/// <param name="AllocatedBytes">The bytes allocated during the measured passes, on every thread.</param>
/// <param name="Gc">The garbage collections during the measured passes.</param>
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