# Benchmarks

Everything under `benchmarks/` measures; everything under `tests/` asserts. There are two kinds of benchmark here.

| Project | Measures | Needs | Run with |
|---|---|---|---|
| `DomainBlocks.EventStore.Benchmarks` | Event codec encode and decode, in process, no I/O (BenchmarkDotNet) | nothing | `dotnet run -c Release --project benchmarks/DomainBlocks.EventStore.Benchmarks` |
| `DomainBlocks.EventStore.NoOp.Benchmarks` | The append benchmark harness's own ceiling, against a store whose appends do nothing | nothing | `dotnet test benchmarks/DomainBlocks.EventStore.NoOp.Benchmarks -c Release --filter FullyQualifiedName~Benchmarks` |
| `DomainBlocks.EventStore.PostgreSQL.Benchmarks` | The PostgreSQL store end to end, plus live subscription latency and an append batch size sweep | Docker | `dotnet test benchmarks/DomainBlocks.EventStore.PostgreSQL.Benchmarks -c Release --filter FullyQualifiedName~Benchmarks` |
| `DomainBlocks.EventStore.MongoDB.Benchmarks` | The MongoDB store end to end | Docker | `dotnet test benchmarks/DomainBlocks.EventStore.MongoDB.Benchmarks -c Release --filter FullyQualifiedName~Benchmarks` |
| `DomainBlocks.EventStore.KurrentDB.Benchmarks` | The KurrentDB store end to end, and the raw `KurrentDBClient` as a baseline | Docker | `dotnet test benchmarks/DomainBlocks.EventStore.KurrentDB.Benchmarks -c Release --filter FullyQualifiedName~Benchmarks` |

## End-to-end benchmarks

The per-store projects are NUnit assemblies that share one harness, `DomainBlocks.Benchmarking`:

- `EventStoreBenchmarkTests<TStreamPos, TLogPos>` is the suite every store runs. A store's project binds it to the
  store's test harness in one line, the same way the contract tests in `tests/` do.
- `AppendBenchmarkRunner` drives the operations: single in-flight for latency, a fixed number of closed-loop workers
  for throughput. `BenchmarkReport` prints the environment, the store's settings, HdrHistogram percentiles and GC
  counts to the test output.
- `NoOpEventStore` is the yardstick. A store figure close to the no-op figure is a harness limit, not a store limit.

Every benchmark is `[Explicit]` and in the `Benchmark` category. A plain `dotnet test` (as in CI) discovers the
assemblies but runs nothing. The NUnit adapter only runs explicit tests under a name filter, so select them by
namespace, which ends in `Benchmarks` for every benchmark project:

```shell
dotnet test benchmarks/DomainBlocks.EventStore.PostgreSQL.Benchmarks -c Release \
  --filter FullyQualifiedName~Benchmarks --logger "console;verbosity=detailed"
```

Narrow the filter to run one case, e.g. `--filter "FullyQualifiedName~Benchmarks&Name~MeasureLatency"`. A category
filter (`TestCategory=Benchmark`) selects the tests but the adapter still skips them as explicit; the category is
there so that other tooling can exclude them. Always build in Release: a Debug build or an attached debugger is
flagged in the report as non-representative.

### Methodology

- Every append writes one small JSON event to a new stream, the cheapest possible append, so the figures are a
  store's ceiling rather than a workload.
- `AppendAsync_MeasureLatency` runs one append at a time after a warm-up on the same code path and reports
  percentiles over 10,000 samples.
- `AppendAsync_MeasureThroughput` runs a fixed number of closed-loop workers (1 to 1,000 in flight, over 1 or 4 store
  instances), warms up for 5 s and measures for 15 s by snapshotting a completion counter, so no in-flight append is
  cancelled or double counted. It also reports per-second stability and latency under that load. The throughput
  ceiling is the plateau across the cases.
- The log is emptied before each benchmark, so a result does not depend on which benchmarks ran before it.
- Each report starts with the environment (OS, CPU count, runtime, GC mode, build configuration) and whatever the
  store's harness reports as affecting the result, such as batch sizes and server settings.

Results depend heavily on the machine and on the container's default server configuration. See the store's README
for measured figures and what drives them, e.g. [PostgreSQL](../src/DomainBlocks.EventStore.PostgreSQL/README.md#benchmarks).

### Environment variables

| Variable | Effect |
|---|---|
| `DBX_POSTGRES_IMAGE` | PostgreSQL image for the container, default `postgres:17` |
| `DBX_TEST_LOG_LEVEL` | Minimum log level written to the test output, default `Debug`; `Trace` shows per-batch append logging |

### Adding a store

Derive a fixture from `EventStoreBenchmarkTests` with the store's harness from `tests/DomainBlocks.Testing.Integration.<Store>`,
in a new `DomainBlocks.EventStore.<Store>.Benchmarks` project with a two-line `SetUpFixture` that starts the store's
test environment. Store-specific benchmarks go in the same fixture, marked `[Explicit("Benchmark")]`; the category is
inherited.