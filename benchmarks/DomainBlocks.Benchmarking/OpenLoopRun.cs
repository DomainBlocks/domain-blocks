using System.Diagnostics;
using HdrHistogram;

namespace DomainBlocks.Benchmarking;

/// <summary>
/// One open-loop run. A dedicated thread issues each arrival at its scheduled time, and the operations complete on the
/// thread pool.
/// </summary>
internal sealed class OpenLoopRun(BenchmarkOperation operation, OpenLoopOptions options, HistogramBase latencies)
{
    // Short enough that an arrival is issued within a few microseconds of its scheduled time.
    private const int SpinIterations = 10;

    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long _completed;
    private long _inFlight;
    private int _errors;
    private bool _issued;

    // Written only by the issuing thread, and read once it has ended.
    private long _scheduled;
    private long _maxInFlight;
    private long _maxIssueLag;
    private bool _stoppedEarly;
    private long _windowStart;
    private long _windowEnd;
    private long _windowStartCount;
    private long _windowEndCount;
    private GcSnapshot _gcBefore;
    private GcSnapshot _gc;

    /// <summary>
    /// Issues the arrivals until the measurement window ends, the run is overloaded, or cancellation is requested.
    /// </summary>
    public Task IssueAsync(CancellationToken cancellationToken)
    {
        var issued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // A thread of its own, rather than a thread-pool thread, so that busy operations can't delay the arrivals.
        var thread = new Thread(() =>
        {
            try
            {
                Issue(cancellationToken);
                issued.SetResult();
            }
            catch (Exception ex)
            {
                issued.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Open-loop arrivals",
            Priority = ThreadPriority.AboveNormal
        };

        thread.Start();
        return issued.Task;
    }

    /// <summary>
    /// Waits for the operations still in flight to finish.
    /// </summary>
    /// <exception cref="TimeoutException">Operations were still in flight after the drain timeout elapsed.</exception>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _issued, true);

        if (Interlocked.Read(ref _inFlight) == 0)
            _drained.TrySetResult();

        try
        {
            await _drained.Task.WaitAsync(options.DrainTimeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"{Interlocked.Read(ref _inFlight):N0} operation(s) were still in flight after the arrivals stopped " +
                $"and the {options.DrainTimeout.TotalSeconds} s drain timeout elapsed.",
                ex);
        }
    }

    public OpenLoopResult ToResult(TimeSpan oneMillisecondDelay) => new(
        options.Rate,
        _scheduled,
        _windowEndCount - _windowStartCount,
        _windowStart == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_windowStart, _windowEnd),
        latencies,
        Volatile.Read(ref _errors),
        _maxInFlight,
        FromTicks(_maxIssueLag),
        _stoppedEarly,
        oneMillisecondDelay,
        _gc);

    private void Issue(CancellationToken cancellationToken)
    {
        // Gaps drawn from an exponential distribution make the arrivals a Poisson process, as the calls of many
        // independent callers are, so that they come in random bursts rather than evenly spaced.
        var random = new Random(options.Seed);
        var meanGap = Stopwatch.Frequency / options.Rate;

        var start = Stopwatch.GetTimestamp();
        var windowStart = start + ToTicks(options.WarmUp);
        var windowEnd = windowStart + ToTicks(options.Measure);
        var next = (double)start;
        var inWindow = false;

        for (var index = 0; ; index++)
        {
            next += -Math.Log(1 - random.NextDouble()) * meanGap;
            var scheduled = (long)next;
            long now;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                now = Stopwatch.GetTimestamp();

                if (!inWindow && now >= windowStart)
                {
                    BeginWindow(now);
                    inWindow = true;
                }

                if (now >= windowEnd)
                {
                    EndWindow(now);
                    CountUnissued(scheduled, windowStart, windowEnd, random, meanGap);
                    return;
                }

                if (now >= scheduled)
                    break;

                Thread.SpinWait(SpinIterations);
            }

            if (Interlocked.Read(ref _inFlight) >= options.MaxInFlight)
            {
                _stoppedEarly = true;

                if (inWindow)
                    EndWindow(now);

                return;
            }

            _maxIssueLag = Math.Max(_maxIssueLag, now - scheduled);
            _maxInFlight = Math.Max(_maxInFlight, Interlocked.Increment(ref _inFlight));

            var measured = scheduled >= windowStart;

            if (measured)
                _scheduled++;

            _ = RunAsync(index, scheduled, measured);
        }
    }

    // Arrivals that fell due in the window but weren't issued by its end still count as scheduled, so that an issuer
    // that falls behind can't make a rate look sustained.
    private void CountUnissued(long scheduled, long windowStart, long windowEnd, Random random, double meanGap)
    {
        var next = (double)scheduled;

        while (scheduled < windowEnd)
        {
            if (scheduled >= windowStart)
                _scheduled++;

            next += -Math.Log(1 - random.NextDouble()) * meanGap;
            scheduled = (long)next;
        }
    }

    private async Task RunAsync(int index, long scheduled, bool measured)
    {
        try
        {
            // A token shared by every operation would take a registration from each one in flight, which costs
            // throughput at high concurrency. Cancelling the run stops the arrivals instead.
            await operation(index, CancellationToken.None);

            var latency = Stopwatch.GetTimestamp() - scheduled;
            Interlocked.Increment(ref _completed);

            if (measured)
                latencies.RecordValue(Math.Min(latency, latencies.HighestTrackableValue));
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _errors);
        }
        finally
        {
            if (Interlocked.Decrement(ref _inFlight) == 0 && Volatile.Read(ref _issued))
                _drained.TrySetResult();
        }
    }

    private void BeginWindow(long now)
    {
        _windowStart = now;
        _windowStartCount = Interlocked.Read(ref _completed);
        _gcBefore = GcSnapshot.Capture();
    }

    private void EndWindow(long now)
    {
        _windowEnd = now;
        _windowEndCount = Interlocked.Read(ref _completed);
        _gc = GcSnapshot.Capture().Since(_gcBefore);
    }

    private static long ToTicks(TimeSpan duration) => (long)(duration.TotalSeconds * Stopwatch.Frequency);

    private static TimeSpan FromTicks(long ticks) => TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
}