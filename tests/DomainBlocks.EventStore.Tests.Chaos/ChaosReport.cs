using System.Text;

namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Represents the result of checking a run: the invariants that it broke, and counts that show how hard its faults
/// hit.
/// </summary>
internal sealed class ChaosReport(RunRecord run, IReadOnlyList<Violation> violations)
{
    private const int ListedViolationCount = 20;

    public IReadOnlyList<Violation> Violations { get; } = violations;

    /// <summary>
    /// Gets the number of operations that needed more than one attempt. A run without any left no append in doubt, so
    /// it proves little.
    /// </summary>
    public int RetriedOperationCount { get; } = run.Operations.Count(x => x.Errors.Count > 0);

    public override string ToString()
    {
        var writtenOperations = run.Log.Select(x => x.Operation).ToHashSet();
        var okCount = run.Operations.Count(x => x.Outcome == AppendOutcome.Ok);

        var indeterminateWrittenCount = run.Operations.Count(x =>
            x.Outcome == AppendOutcome.Indeterminate && writtenOperations.Contains(x.Number));

        var errorCounts = run.Operations
            .SelectMany(x => x.Errors)
            .GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .Select(x => $"{x.Key} {x.Count()}");

        var report = new StringBuilder()
            .AppendLine(
                $"Operations: {run.Operations.Count} ({okCount} ok, {run.Operations.Count - okCount} " +
                $"indeterminate, of which {indeterminateWrittenCount} written); {RetriedOperationCount} retried")
            .AppendLine($"Failed attempts: {string.Join(", ", errorCounts.DefaultIfEmpty("none"))}")
            .AppendLine(
                $"Events: {run.Log.Count} in the log, {run.Received.Count} received; the subscription fell behind " +
                $"{run.FellBehindCount} times");

        if (Violations.Count == 0)
            return report.Append("Violations: none").ToString();

        report.AppendLine($"Violations: {Violations.Count}, listing up to {ListedViolationCount}:");

        foreach (var violation in Violations.Take(ListedViolationCount))
            report.AppendLine($"  {violation}");

        return report.ToString();
    }
}