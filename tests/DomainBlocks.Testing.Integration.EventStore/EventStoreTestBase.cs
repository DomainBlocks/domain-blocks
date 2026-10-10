using DomainBlocks.EventStore;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;

namespace DomainBlocks.Testing.Integration.EventStore;

/// <summary>
/// The base of every shared suite. It owns the store's lifecycle through a backend harness, so a concrete fixture only
/// has to say which harness it uses.
/// </summary>
public abstract class EventStoreTestBase<TStreamPos, TLogPos>(IEventStoreTestHarness<TStreamPos, TLogPos> harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    protected IEventStoreTestHarness<TStreamPos, TLogPos> Harness { get; } = harness;

    /// <summary>
    /// Gets a value that indicates whether the event log is emptied before each test. Suites whose tests assume an
    /// empty log override this, and the rest isolate their tests with unique stream IDs.
    /// </summary>
    protected virtual bool ResetLogBeforeEachTest => false;

    [OneTimeSetUp]
    public Task InitializeStoreAsync() => Harness.InitializeAsync(TestStoreName.For(this));

    [OneTimeTearDown]
    public Task DropStoreAsync() => Harness.DropAsync();

    [SetUp]
    public async Task ResetLogIfRequiredAsync()
    {
        if (ResetLogBeforeEachTest)
            await Harness.ResetAsync();
    }

    protected IEventStore<object, string, TStreamPos, TLogPos> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        return Harness.CreateEventStore(
            eventTypeMap,
            eventFormat,
            contractMappers,
            loggerNameSuffix);
    }

    /// <summary>
    /// Ignores the current test, with the reason in the results, when the store lacks a capability it needs.
    /// </summary>
    protected void RequireCapability(StoreCapabilities capability)
    {
        if (!Harness.Capabilities.HasFlag(capability))
            Assert.Ignore($"The store does not support {capability}.");
    }

    /// <summary>
    /// Ignores the current test, with the reason in the results, when the store has a capability that the test expects
    /// it to lack.
    /// </summary>
    protected void RequireNoCapability(StoreCapabilities capability)
    {
        if (Harness.Capabilities.HasFlag(capability))
            Assert.Ignore($"The store supports {capability}.");
    }

    protected TStreamPos CreateStreamPosition(ulong value) => Harness.CreateStreamPosition(value);

    protected TLogPos CreateLogPosition(ulong value) => Harness.CreateLogPosition(value);
}