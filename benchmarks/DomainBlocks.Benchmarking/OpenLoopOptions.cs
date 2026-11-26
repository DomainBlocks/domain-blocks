using System.Globalization;

namespace DomainBlocks.Benchmarking;

public sealed record OpenLoopOptions
{
    /// <summary>
    /// The environment variable that <see cref="FromEnvironment"/> reads the rate from, in operations per second.
    /// </summary>
    public const string RateEnvironmentVariable = "DBX_BENCHMARK_RATE";

    /// <summary>The rate that <see cref="FromEnvironment"/> uses when the environment variable isn't set.</summary>
    public const double DefaultRate = 10_000;

    /// <summary>The mean arrival rate, in operations per second.</summary>
    public required double Rate { get; init; }

    public TimeSpan WarmUp { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan Measure { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait after the measurement window for the operations still in flight.</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The number of operations in flight at which arrivals stop and the run ends early, unsustained.
    /// </summary>
    public int MaxInFlight { get; init; } = 100_000;

    /// <summary>The seed for the arrival times, so that every run at a rate offers the same arrivals.</summary>
    public int Seed { get; init; }

    /// <summary>
    /// Returns the options with the rate from <see cref="RateEnvironmentVariable"/>, or <see cref="DefaultRate"/> if it
    /// isn't set, so that a script can run one benchmark at many rates.
    /// </summary>
    public static OpenLoopOptions FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(RateEnvironmentVariable);

        if (string.IsNullOrEmpty(value))
            return new OpenLoopOptions { Rate = DefaultRate };

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) || rate <= 0)
        {
            throw new InvalidOperationException(
                $"{RateEnvironmentVariable} must be a positive number of operations per second, not '{value}'.");
        }

        return new OpenLoopOptions { Rate = rate };
    }
}