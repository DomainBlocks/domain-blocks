using HdrHistogram;

namespace DomainBlocks.Benchmarking;

/// <summary>
/// The result of an open-loop run.
/// </summary>
/// <param name="Rate">The mean arrival rate offered, in operations per second.</param>
/// <param name="Scheduled">
/// The arrivals scheduled within the measurement window, including any not yet issued when it ended.
/// </param>
/// <param name="Completed">
/// The operations that completed within the window. Those include operations that arrived during the warm-up, so a run
/// that keeps up can complete slightly more than <paramref name="Scheduled"/>.
/// </param>
/// <param name="Duration">The length of the window, or zero if arrivals stopped before it began.</param>
/// <param name="Latencies">
/// For each arrival scheduled within the window, the time from its scheduled arrival to its completion, in
/// <see cref="System.Diagnostics.Stopwatch"/> ticks.
/// </param>
/// <param name="Errors">The operations that failed.</param>
/// <param name="MaxInFlight">The most operations in flight at once.</param>
/// <param name="MaxIssueLag">
/// The longest time by which an arrival was issued after its scheduled time. It's normally a few microseconds. More
/// means that the issuing thread fell behind, and the lateness counts in the latencies.
/// </param>
/// <param name="StoppedEarly">
/// Whether arrivals stopped because <see cref="OpenLoopOptions.MaxInFlight"/> operations were in flight.
/// </param>
/// <param name="OneMillisecondDelay">
/// The median time that a 1 ms delay took at the start of the run, which shows the timer resolution the run had.
/// </param>
/// <param name="Gc">The garbage collections during the window.</param>
public sealed record OpenLoopResult(
    double Rate,
    long Scheduled,
    long Completed,
    TimeSpan Duration,
    HistogramBase Latencies,
    int Errors,
    long MaxInFlight,
    TimeSpan MaxIssueLag,
    bool StoppedEarly,
    TimeSpan OneMillisecondDelay,
    GcSnapshot Gc)
{
    /// <summary>
    /// The lowest <see cref="CompletedFraction"/> at which the rate is sustained.
    /// </summary>
    public const double SustainedFraction = 0.99;

    /// <summary>
    /// Gets the operations completed in the window, as a share of the arrivals scheduled in it. Completions are the
    /// arrivals issued less the growth in the operations in flight, so a fraction well below one means a growing
    /// backlog, or arrivals that weren't issued on time.
    /// </summary>
    public double CompletedFraction => Scheduled == 0 ? 0 : (double)Completed / Scheduled;

    public double OpsPerSecond => Duration == TimeSpan.Zero ? 0 : Completed / Duration.TotalSeconds;

    /// <summary>
    /// Gets a value that indicates whether the system kept up with the rate: arrivals never stopped early, and at least
    /// <see cref="SustainedFraction"/> of the window's arrivals completed in it.
    /// </summary>
    public bool IsSustained => !StoppedEarly && CompletedFraction >= SustainedFraction;
}