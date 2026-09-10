namespace DomainBlocks.Testing.Integration;

public static class TestTimeouts
{
    public const int DefaultMillis = 30 * 1_000;

    /// <summary>
    /// Benchmarks warm up and then measure for tens of seconds per case; this is a safety net, not a budget.
    /// </summary>
    public const int BenchmarkMillis = 5 * 60 * 1_000;
}
