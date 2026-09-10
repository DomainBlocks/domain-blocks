using System.Diagnostics;
using System.Numerics;

namespace DomainBlocks.Testing.Integration.Benchmarking;

/// <summary>
/// An allocation-free latency histogram with microsecond resolution and roughly 1.5% relative precision (64
/// sub-buckets per power of two). Not thread-safe: use one instance per worker and <see cref="Merge"/> at the end.
/// </summary>
public sealed class LatencyHistogram
{
    private const int SubBucketBits = 6;
    private const int SubBucketCount = 1 << SubBucketBits;
    private const int MaxMagnitude = 40; // 2^40 µs ≈ 12.7 days
    private const int BucketCount = SubBucketCount + ((MaxMagnitude - SubBucketBits) * SubBucketCount);

    private static readonly double TicksPerMicrosecond = Stopwatch.Frequency / 1_000_000d;

    private readonly long[] _counts = new long[BucketCount];
    private double _sumSquaresMicros;

    public long Count { get; private set; }

    public long SumMicros { get; private set; }

    public long MinMicros { get; private set; } = long.MaxValue;

    public long MaxMicros { get; private set; }

    public double MeanMillis => Count == 0 ? 0 : SumMicros / (double)Count / 1_000;

    public double MinMillis => Count == 0 ? 0 : MinMicros / 1_000d;

    public double MaxMillis => MaxMicros / 1_000d;

    public double StdDevMillis
    {
        get
        {
            if (Count < 2)
                return 0;

            var mean = SumMicros / (double)Count;
            var variance = (_sumSquaresMicros / Count) - (mean * mean);
            return Math.Sqrt(Math.Max(variance, 0)) / 1_000;
        }
    }

    /// <summary>
    /// Records an elapsed interval expressed as a difference between two <see cref="Stopwatch.GetTimestamp"/> values.
    /// </summary>
    public void RecordStopwatchTicks(long ticks) => RecordMicros((long)(ticks / TicksPerMicrosecond));

    public void Record(TimeSpan elapsed) => RecordMicros(elapsed.Ticks / (TimeSpan.TicksPerMillisecond / 1_000));

    public void RecordMicros(long micros)
    {
        if (micros < 0)
            micros = 0;

        _counts[IndexOf(micros)]++;
        Count++;
        SumMicros += micros;
        _sumSquaresMicros += (double)micros * micros;

        if (micros < MinMicros)
            MinMicros = micros;

        if (micros > MaxMicros)
            MaxMicros = micros;
    }

    public void Merge(LatencyHistogram other)
    {
        for (var i = 0; i < BucketCount; i++)
            _counts[i] += other._counts[i];

        Count += other.Count;
        SumMicros += other.SumMicros;
        _sumSquaresMicros += other._sumSquaresMicros;
        MinMicros = Math.Min(MinMicros, other.MinMicros);
        MaxMicros = Math.Max(MaxMicros, other.MaxMicros);
    }

    /// <summary>
    /// Returns the value at the given percentile (0 to 1) in milliseconds. The result is the highest value equivalent
    /// to the bucket containing the percentile, so it is never below the true value by more than the bucket precision.
    /// </summary>
    public double PercentileMillis(double percentile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percentile);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 1);

        if (Count == 0)
            return 0;

        var target = Math.Max(1, (long)Math.Ceiling(percentile * Count));
        long seen = 0;

        for (var i = 0; i < BucketCount; i++)
        {
            seen += _counts[i];

            if (seen >= target)
                return Math.Min(HighestEquivalentMicros(i), MaxMicros) / 1_000d;
        }

        return MaxMillis;
    }

    private static int IndexOf(long micros)
    {
        if (micros < SubBucketCount)
            return (int)micros;

        var magnitude = BitOperations.Log2((ulong)micros);

        if (magnitude >= MaxMagnitude)
            return BucketCount - 1;

        var shift = magnitude - SubBucketBits;
        var subBucket = (int)(micros >> shift) & (SubBucketCount - 1);
        return SubBucketCount + (shift * SubBucketCount) + subBucket;
    }

    private static long HighestEquivalentMicros(int index)
    {
        if (index < SubBucketCount)
            return index;

        var shift = (index - SubBucketCount) / SubBucketCount;
        var subBucket = (index - SubBucketCount) % SubBucketCount;
        var lowest = (long)(SubBucketCount + subBucket) << shift;
        return lowest + (1L << shift) - 1;
    }
}
