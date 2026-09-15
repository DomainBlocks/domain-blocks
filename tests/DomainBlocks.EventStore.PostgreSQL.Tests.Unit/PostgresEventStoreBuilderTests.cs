using DomainBlocks.EventStore.TypeMapping;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

/// <summary>
/// The builder's validation, which needs no server: nothing in Build touches the database.
/// </summary>
public class PostgresEventStoreBuilderTests
{
    private sealed record OrderPlaced(string OrderId);

    [Test]
    public void Build_WithoutConnection_ThrowsNamingTheMethodsToCall()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            new PostgresEventStoreBuilder<object>().MapEvent<OrderPlaced>().Build());

        ex.Message.ShouldContain("UseDataSource");
        ex.Message.ShouldContain("UseConnectionString");
    }

    [Test]
    public void Build_WithoutMappings_ThrowsNamingMapEvents()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            new PostgresEventStoreBuilder<object>().UseConnectionString("Host=localhost").Build());

        ex.Message.ShouldContain("MapEvents");
    }

    [Test]
    public void UseDataSource_AfterUseConnectionString_Throws()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = new PostgresEventStoreBuilder<object>().UseConnectionString("Host=localhost");

        Should.Throw<InvalidOperationException>(() => builder.UseDataSource(dataSource));
    }

    [Test]
    public void UseConnectionString_AfterUseDataSource_Throws()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = new PostgresEventStoreBuilder<object>().UseDataSource(dataSource);

        Should.Throw<InvalidOperationException>(() => builder.UseConnectionString("Host=localhost"));
    }

    [Test]
    public async Task ConfigureOptions_IsAppliedToTheStoreOptions()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var seen = new List<string>();

        await using var store = new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .ConfigureOptions(o => o.Schema = "first")
            .ConfigureOptions(o => seen.Add(o.Schema))
            .MapEvents(EventTypeMapping.ReadWrite<OrderPlaced>())
            .Build();

        seen.ShouldBe(["first"]);
    }

    [Test]
    public async Task Build_BorrowedDataSource_CanBuildMoreThanOneStore()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var builder = new PostgresEventStoreBuilder<object>().UseDataSource(dataSource).MapEvent<OrderPlaced>();

        await using var first = builder.Build();
        await using var second = builder.Build();

        second.ShouldNotBeSameAs(first);
    }
}