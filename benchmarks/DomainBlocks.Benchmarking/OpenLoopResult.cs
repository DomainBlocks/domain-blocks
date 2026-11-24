using HdrHistogram;

namespace DomainBlocks.Benchmarking;

/// <summary>
/// The result of an open-loop run. <see cref="Scheduled"/> counts the arrivals scheduled in the measurement window, and
/// <see cref="Completed"/> the operations that completed in it, whenever they arrived. <see cref="Latencies"/> holds,
/// for each arrival in the window, the time from its scheduled arrival to its completion.
/// </summary>
public sealed record OpenLoopResult(
    double Rate,
    long Scheduled,
    long Completed,
    TimeSpan Duration,
    HistogramBase Latencies,
    int Errors,
    long MaxOutstanding,
    TimeSpan MaxIssueLag,
    bool StoppedEarly,
    long Undrained,
    TimeSpan TimerProbe,
    GcSnapshot Gc)
{
    /// <summary>
    /// The lowest <see cref="CompletedFraction"/> at which the rate is sustained.
    /// </summary>
    public const double SustainedFraction = 0.99;

    /// <summary>
    /// The operations completed in the window, as a share of the arrivals scheduled in it. Completions are the
    /// arrivals issued less the growth in the operations in flight, so a fraction well below one means a growing
    /// backlog, or arrivals that weren't issued on time.
    /// </summary>
    public double CompletedFraction => Scheduled == 0 ? 0 : (double)Completed / Scheduled;

    public double OpsPerSecond => Duration == TimeSpan.Zero ? 0 : Completed / Duration.TotalSeconds;

    /// <summary>
    /// Whether the system kept up with the rate: arrivals never stopped early, at least
    /// <see cref="SustainedFraction"/> of the window's arrivals completed in it, and every operation finished.
    /// </summary>
    public bool IsSustained => !StoppedEarly && Undrained == 0 && CompletedFraction >= SustainedFraction;
}