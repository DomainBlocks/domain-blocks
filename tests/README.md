# Tests

Everything under `tests/` asserts behaviour; benchmarks live under [`benchmarks/`](../benchmarks/README.md).

| Project name | Contains | Needs |
|---|---|---|
| `*.Tests.Unit` | Tests with no external dependencies | nothing |
| `*.Tests.Integration` | Tests against a real store in a throw-away container (Testcontainers) | Docker |
| `DomainBlocks.Testing` | NUnit logging support shared by every test project | |
| `DomainBlocks.Testing.Integration` | The event store **contract**: abstract suites every store must pass, and the harness they run through | |
| `DomainBlocks.Testing.Integration.<Store>` | One store's container, test codec and harness | |

A plain `dotnet test` runs every unit and integration test, which is what CI does.

## How the integration tests are organised

Each `DomainBlocks.EventStore.<Store>.Tests.Integration` project has the same shape:

| Folder | Holds | Rule |
|---|---|---|
| `Contract/` | One class per shared suite, binding it to the store's harness | No test methods here: a class is one line |
| root | Tests of behaviour only this store has | If another store could pass it, it belongs in the contract |
| `Support/` | The assembly `SetUpFixture`, helpers and a base class for the store-specific tests | |

The shared suites in `DomainBlocks.Testing.Integration/Contract`:

| Suite | Covers |
|---|---|
| `EventStoreTests` | Append with expected state, conflicts, idempotent commit ids, metadata, cancellation; reading a stream in both directions, from a position, with a maximum count, with and without metadata |
| `EventStoreReadAllTests` | Reading the whole log: order, positions, origins, options |
| `EventStoreSubscriptionTests` | Catch-up and live subscriptions to the log and to a stream, back-pressure, cancellation |
| `EventStoreConcurrencyTests` | Several store instances appending at once: contiguity and exactly-one-winner conflicts |
| `EventStoreEventRepresentationTests` | Type mapping, contract mappers and read transforms |
| `EventStoreEventFormatTests` | A round trip in every event format the store's codec supports |

A suite derives from `EventStoreTestBase` and asks its `IEventStoreHarness` for everything backend-specific: creating
and dropping the fixture's schema or database (named after the fixture), emptying the log before a test when the
suite needs that, building a store, and constructing positions. A store that lacks an optional behaviour declares it
in `StoreCapabilities`; a test that needs the behaviour is reported as ignored for that store rather than failing.
The same goes for event formats the store's test codec cannot produce.

### Adding a contract test

Add it to the suite in `DomainBlocks.Testing.Integration/Contract`. It runs for every store on the next build. If a
store fails it, either the store has a bug or the test assumes something the contract does not promise; fix one or
the other rather than moving the test to a store-specific class.

### Adding a store

1. Create `DomainBlocks.Testing.Integration.<Store>` with a `<Store>TestEnvironment` (starts the container, exposes the
   client and logger factory) and a `<Store>EventStoreHarness` implementing `IEventStoreHarness`.
2. Create `DomainBlocks.EventStore.<Store>.Tests.Integration` with a two-line `SetUpFixture` and one one-line class per
   suite in `Contract/`.
3. Add store-specific tests at the root as needed.

## Running

```shell
dotnet test                                                        # everything
dotnet test tests/DomainBlocks.EventStore.PostgreSQL.Tests.Integration   # one store
dotnet test --filter FullyQualifiedName~Contract                  # the contract suites for every store
```

| Variable | Effect |
|---|---|
| `DBX_POSTGRES_IMAGE` | PostgreSQL image for the container, default `postgres:17`, e.g. to test the minimum supported version |
| `DBX_TEST_LOG_LEVEL` | Minimum log level written to the test output, default `Debug` |