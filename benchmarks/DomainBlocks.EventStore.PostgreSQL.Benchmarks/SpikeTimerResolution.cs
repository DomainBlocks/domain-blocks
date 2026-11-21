using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DomainBlocks.EventStore.PostgreSQL.Benchmarks;

// SPIKE: DBX_SPIKE_TIMER1=1 raises the Windows timer resolution to 1 ms for the test process, so short delays aren't
// rounded up to the ~15.6 ms tick (memory windows-timer-resolution).
internal static class SpikeTimerResolution
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("DBX_SPIKE_TIMER1") == "1" && OperatingSystem.IsWindows())
            _ = TimeBeginPeriod(1);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);
}