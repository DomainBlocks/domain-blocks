using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DomainBlocks.Benchmarking;

/// <summary>
/// Raises the Windows timer resolution to 1 ms until disposed, and does nothing on other platforms.
/// </summary>
/// <remarks>
/// .NET timers on Windows round up to the system clock's tick, about 15.6 ms by default, so a 1 ms delay waits about
/// 15.6 ms. The raise applies to every timer in the process, including the system under test's.
/// </remarks>
internal sealed class TimerResolution : IDisposable
{
    // An odd count has one middle sample. 21 lets the median ignore the few delays the scheduler stretches, and takes
    // about a third of a second at the default tick.
    public const int ProbeSampleCount = 21;

    private const uint PeriodMillis = 1;

    private readonly bool _raised;

    private TimerResolution(bool raised)
    {
        _raised = raised;
    }

    public static TimerResolution Raise() => new(OperatingSystem.IsWindows() && TimeBeginPeriod(PeriodMillis) == 0);

    /// <summary>
    /// Returns the median time that a 1 ms <see cref="Task.Delay(int, CancellationToken)"/> takes, so that a report can
    /// show the timer resolution its results were measured with.
    /// </summary>
    public static async Task<TimeSpan> ProbeAsync(CancellationToken cancellationToken)
    {
        var samples = new TimeSpan[ProbeSampleCount];

        for (var i = 0; i < samples.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            await Task.Delay(1, cancellationToken);
            samples[i] = Stopwatch.GetElapsedTime(start);
        }

        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    public void Dispose()
    {
        if (_raised)
            _ = TimeEndPeriod(PeriodMillis);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);
}