namespace DomainBlocks.Benchmarking;

/// <summary>
/// One operation against the system under test. <paramref name="workerIndex"/> lets callers spread workers over several
/// instances without synchronization.
/// </summary>
public delegate Task BenchmarkOperation(int workerIndex, CancellationToken cancellationToken);