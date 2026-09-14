using System.Diagnostics;
using HdrHistogram;

namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// Store-agnostic closed-loop benchmark harness. Every run uses a fresh stream id per operation, generated outside the
/// timed region, so the store sees the same "new stream" path during warm-up and measurement. Latencies are recorded
/// in <see cref="Stopwatch"/> ticks to an HdrHistogram with microsecond resolution and three significant digits.
/// </summary>
public sealed class AppendBenchmarkRunner
{
    private const int SignificantDigits = 3;
    private static readonly long LowestTrackableTicks = Math.Max(1, Stopwatch.Frequency / 1_000_000);
    private static readonly long HighestTrackableTicks = TimeStamp.Hours(1);

    private readonly string _runId = $"bench-{Guid.NewGuid():N}";
    private long _nextStreamNumber;
    private bool _stop;
    private bool _measuring;

    /// <summary>
    /// Measures unloaded latency: one operation in flight, sequentially, on a single worker.
    /// </summary>
    public async Task<LatencyResult> MeasureLatencyAsync(
        BenchmarkOperation operation,
        LatencyOptions options,
        CancellationToken cancellationToken)
    {
        var warmUpStart = Stopwatch.GetTimestamp();
        var warmUpOps = 0;

        while (warmUpOps < options.MinWarmUpOperations || Stopwatch.GetElapsedTime(warmUpStart) < options.WarmUp)
        {
            await operation(0, NextStreamId(), cancellationToken);
            warmUpOps++;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var latencies = new LongHistogram(LowestTrackableTicks, HighestTrackableTicks, SignificantDigits);
        var errors = 0;
        var gcBefore = GcSnapshot.Capture();
        var start = Stopwatch.GetTimestamp();

        for (var i = 0; i < options.SampleCount && Stopwatch.GetElapsedTime(start) < options.MaxDuration; i++)
        {
            var streamId = NextStreamId();
            var operationStart = Stopwatch.GetTimestamp();

            try
            {
                await operation(0, streamId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                errors++;
                continue;
            }

            latencies.RecordValue(Stopwatch.GetTimestamp() - operationStart);
        }

        var duration = Stopwatch.GetElapsedTime(start);
        return new LatencyResult(latencies, duration, errors, GcSnapshot.Capture().Since(gcBefore));
    }

    /// <summary>
    /// Measures throughput with <see cref="ThroughputOptions.InFlight"/> closed-loop workers. Throughput is derived from
    /// snapshots of a completion counter at the start and end of the measurement window, so operations in flight at the
    /// window edges neither inflate nor deflate the result, and no in-flight operation is ever cancelled.
    /// </summary>
    public async Task<ThroughputResult> MeasureThroughputAsync(
        BenchmarkOperation operation,
        ThroughputOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.InFlight);

        Volatile.Write(ref _stop, false);
        Volatile.Write(ref _measuring, false);

        var completed = 0L;
        var errors = 0;
        var latencies = new LongConcurrentHistogram(LowestTrackableTicks, HighestTrackableTicks, SignificantDigits);
        var workers = new Task[options.InFlight];

        for (var i = 0; i < options.InFlight; i++)
        {
            var workerIndex = i;

            workers[i] = Task.Run(
                async () =>
                {
                    while (!Volatile.Read(ref _stop))
                    {
                        var streamId = NextStreamId();
                        var operationStart = Stopwatch.GetTimestamp();

                        try
                        {
                            var task = operation(workerIndex, streamId, cancellationToken);

                            // An operation that completes synchronously would turn this loop into a busy spin that
                            // never yields its thread-pool thread; with many workers that starves the timer
                            // continuations that end the run. Yield so every worker stays fair.
                            if (task.IsCompleted)
                                await Task.Yield();

                            await task;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            Interlocked.Increment(ref errors);
                            continue;
                        }

                        Interlocked.Increment(ref completed);

                        if (Volatile.Read(ref _measuring))
                            latencies.RecordValue(Stopwatch.GetTimestamp() - operationStart);
                    }
                },
                cancellationToken);
        }

        var workersTask = Task.WhenAll(workers);

        try
        {
            await Task.Delay(options.WarmUp, cancellationToken);

            var perSecond = new List<double>((int)options.Measure.TotalSeconds + 1);
            var gcBefore = GcSnapshot.Capture();
            var windowStart = Stopwatch.GetTimestamp();
            var windowStartCount = Volatile.Read(ref completed);
            Volatile.Write(ref _measuring, true);

            var lastTimestamp = windowStart;
            var lastCount = windowStartCount;

            while (Stopwatch.GetElapsedTime(windowStart) < options.Measure)
            {
                var remaining = options.Measure - Stopwatch.GetElapsedTime(windowStart);

                await Task.Delay(remaining < TimeSpan.FromSeconds(1)
                    ? remaining
                    : TimeSpan.FromSeconds(1), cancellationToken);

                var now = Stopwatch.GetTimestamp();
                var count = Volatile.Read(ref completed);
                var seconds = Stopwatch.GetElapsedTime(lastTimestamp, now).TotalSeconds;

                if (seconds >= 0.5)
                    perSecond.Add((count - lastCount) / seconds);

                lastTimestamp = now;
                lastCount = count;
            }

            var windowEnd = Stopwatch.GetTimestamp();
            var windowEndCount = Volatile.Read(ref completed);
            Volatile.Write(ref _measuring, false);
            var gc = GcSnapshot.Capture().Since(gcBefore);

            // Ask the workers to stop and let in-flight operations drain naturally.
            Volatile.Write(ref _stop, true);
            await workersTask;

            return new ThroughputResult(
                options.InFlight,
                windowEndCount - windowStartCount,
                Stopwatch.GetElapsedTime(windowStart, windowEnd),
                perSecond,
                latencies,
                Volatile.Read(ref errors),
                gc);
        }
        finally
        {
            Volatile.Write(ref _stop, true);
        }
    }

    private string NextStreamId() => $"{_runId}-{Interlocked.Increment(ref _nextStreamNumber)}";
}