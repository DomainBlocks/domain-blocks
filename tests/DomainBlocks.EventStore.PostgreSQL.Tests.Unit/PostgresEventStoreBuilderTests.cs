using DomainBlocks.Testing.Events;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresEventStoreBuilderTests
{
    private static readonly TimeSpan[] OutOfRangeIntervals =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(-1),
        Timeout.InfiniteTimeSpan,
        TimeSpan.FromDays(1) + TimeSpan.FromTicks(1)
    ];

    private static readonly TimeSpan[] OutOfRangeRetryDelays =
    [
        TimeSpan.FromTicks(-1),
        Timeout.InfiniteTimeSpan,
        TimeSpan.FromDays(1) + TimeSpan.FromTicks(1)
    ];

    [Test]
    public void Build_WithoutConnection_ThrowsNamingTheMethodsToCall()
    {
        var ex = Should.Throw<InvalidOperationException>(() => new PostgresEventStoreBuilder<object>().Build());

        ex.Message.ShouldContain("UseDataSource");
        ex.Message.ShouldContain("UseConnectionString");
    }

    [Test]
    public async Task ConfigureOptions_IsAppliedToTheStoreOptions()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var seen = new List<string>();

        await using var store = new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .ConfigureOptions(o => o.Schema = "first")
            .ConfigureOptions(o => seen.Add(o.Schema))
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();

        seen.ShouldBe(["first"]);
    }

    [TestCaseSource(nameof(OutOfRangeIntervals))]
    public async Task Build_OutOfRangeInactiveSlotTimeout_Throws(TimeSpan timeout)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.InactiveSlotTimeout = timeout);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.InactiveSlotTimeout)}");
    }

    [TestCaseSource(nameof(OutOfRangeIntervals))]
    public async Task Build_OutOfRangeReceiverTimeout_Throws(TimeSpan timeout)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.WalReceiverTimeout = timeout);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.WalReceiverTimeout)}");
    }

    [TestCaseSource(nameof(OutOfRangeIntervals))]
    public async Task Build_OutOfRangeStatusInterval_Throws(TimeSpan interval)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.WalReceiverStatusInterval = interval);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.WalReceiverStatusInterval)}");
    }

    [TestCaseSource(nameof(OutOfRangeRetryDelays))]
    public async Task Build_OutOfRangeRetryDelay_Throws(TimeSpan delay)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.RetryDelay = delay);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.RetryDelay)}");
    }

    [TestCaseSource(nameof(OutOfRangeRetryDelays))]
    public async Task Build_OutOfRangeMaxRetryDelay_Throws(TimeSpan delay)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.MaxRetryDelay = delay);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.MaxRetryDelay)}");
    }

    [Test]
    public async Task Build_OneDayIntervals_Succeeds()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o =>
        {
            o.InactiveSlotTimeout = TimeSpan.FromDays(1);
            o.WalReceiverTimeout = TimeSpan.FromDays(1);
            o.WalReceiverStatusInterval = TimeSpan.FromDays(1);
            o.RetryDelay = TimeSpan.FromDays(1);
            o.MaxRetryDelay = TimeSpan.FromDays(1);
        });

        await using var store = builder.Build();
    }

    [Test]
    public async Task Build_ZeroRetryDelays_Succeeds()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o =>
        {
            o.RetryDelay = TimeSpan.Zero;
            o.MaxRetryDelay = TimeSpan.Zero;
        });

        await using var store = builder.Build();
    }

    private static PostgresEventStoreBuilder<object> CreateBuilder(
        NpgsqlDataSource dataSource,
        Action<PostgresReplicationOptions> configureReplication)
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .ConfigureOptions(o => configureReplication(o.Replication))
            .ConfigureCodec(x => x.MapEvent<TestEvent>());
    }
}