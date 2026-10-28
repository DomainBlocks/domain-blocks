using DomainBlocks.Testing.Events;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresEventStoreBuilderTests
{
    private static readonly TimeSpan LongerThanTimers = TimerDurations.MaxDuration + TimeSpan.FromTicks(1);

    private static readonly TimeSpan[] UnsupportedPeriods =
        [TimerDurations.MinPeriod - TimeSpan.FromTicks(1), LongerThanTimers];

    // Polly rejects retry delays longer than 1 day.
    private static readonly TimeSpan LongerThanPolly = TimeSpan.FromDays(1) + TimeSpan.FromTicks(1);

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

    [Test]
    public async Task Build_TooLongBatchingDelay_Throws()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.AppendBatchingDelay = LongerThanTimers);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresEventStoreOptions.AppendBatchingDelay)}");
    }

    [TestCaseSource(nameof(UnsupportedPeriods))]
    public async Task Build_UnsupportedInactiveSlotTimeout_Throws(TimeSpan timeout)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.Replication.InactiveSlotTimeout = timeout);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.InactiveSlotTimeout)}");
    }

    [Test]
    public async Task Build_TooLongReceiverTimeout_Throws()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.Replication.WalReceiverTimeout = LongerThanTimers);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.WalReceiverTimeout)}");
    }

    [TestCaseSource(nameof(UnsupportedPeriods))]
    public async Task Build_UnsupportedStatusInterval_Throws(TimeSpan interval)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.Replication.WalReceiverStatusInterval = interval);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.WalReceiverStatusInterval)}");
    }

    [Test]
    public async Task Build_TooLongRetryDelay_Throws()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.Replication.RetryDelay = LongerThanPolly);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.RetryDelay)}");
    }

    [Test]
    public async Task Build_TooLongMaxRetryDelay_Throws()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o => o.Replication.MaxRetryDelay = LongerThanPolly);

        var ex = Should.Throw<ArgumentOutOfRangeException>(builder.Build);

        ex.ParamName.ShouldEndWith($".{nameof(PostgresReplicationOptions.MaxRetryDelay)}");
    }

    [Test]
    public async Task Build_LongestSupportedValues_Succeeds()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o =>
        {
            o.AppendBatchingDelay = TimerDurations.MaxDuration;
            o.Replication.InactiveSlotTimeout = TimerDurations.MaxDuration;
            o.Replication.WalReceiverTimeout = TimerDurations.MaxDuration;
            o.Replication.WalReceiverStatusInterval = TimerDurations.MaxDuration;
            o.Replication.RetryDelay = TimeSpan.FromDays(1);
            o.Replication.MaxRetryDelay = TimeSpan.FromDays(1);
        });

        await using var store = builder.Build();
    }

    [Test]
    public async Task Build_ShortestSupportedPeriods_Succeeds()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = CreateBuilder(dataSource, o =>
        {
            o.Replication.InactiveSlotTimeout = TimerDurations.MinPeriod;
            o.Replication.WalReceiverStatusInterval = TimerDurations.MinPeriod;
        });

        await using var store = builder.Build();
    }

    private static PostgresEventStoreBuilder<object> CreateBuilder(
        NpgsqlDataSource dataSource,
        Action<PostgresEventStoreOptions> configure)
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .ConfigureOptions(configure)
            .ConfigureCodec(x => x.MapEvent<TestEvent>());
    }
}