using DomainBlocks.EventStore.TypeMapping;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

/// <summary>
/// The builder's validation, which needs no server: nothing in Build touches the database.
/// </summary>
public class MongoEventStoreBuilderTests
{
    private sealed record OrderPlaced(string OrderId);

    [Test]
    public void Build_WithoutConnection_ThrowsNamingTheMethodsToCall()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            new MongoEventStoreBuilder<object>().MapEvent<OrderPlaced>().Build());

        ex.Message.ShouldContain("UseClient");
        ex.Message.ShouldContain("UseConnectionString");
    }

    [Test]
    public void Build_WithoutMappings_ThrowsNamingMapEvents()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            new MongoEventStoreBuilder<object>().UseConnectionString("mongodb://localhost").Build());

        ex.Message.ShouldContain("MapEvents");
    }

    [Test]
    public void UseClient_AfterUseConnectionString_Throws()
    {
        using var client = new MongoClient("mongodb://localhost");
        var builder = new MongoEventStoreBuilder<object>().UseConnectionString("mongodb://localhost");

        Should.Throw<InvalidOperationException>(() => builder.UseClient(client));
    }

    [Test]
    public void UseConnectionString_AfterUseClient_Throws()
    {
        using var client = new MongoClient("mongodb://localhost");
        var builder = new MongoEventStoreBuilder<object>().UseClient(client);

        Should.Throw<InvalidOperationException>(() => builder.UseConnectionString("mongodb://localhost"));
    }

    [Test]
    public async Task ConfigureOptions_IsAppliedToTheStoreOptions()
    {
        using var client = new MongoClient("mongodb://localhost");
        var seen = new List<string>();

        await using var store = new MongoEventStoreBuilder<object>()
            .UseClient(client)
            .ConfigureOptions(o => o.DatabaseName = "first")
            .ConfigureOptions(o => seen.Add(o.DatabaseName))
            .MapEvents(EventTypeMapping.ReadWrite<OrderPlaced>())
            .Build();

        seen.ShouldBe(["first"]);
    }

    [Test]
    public async Task Build_BorrowedClient_CanBuildMoreThanOneStore()
    {
        using var client = new MongoClient("mongodb://localhost");
        var builder = new MongoEventStoreBuilder<object>().UseClient(client).MapEvent<OrderPlaced>();

        await using var first = builder.Build();
        await using var second = builder.Build();

        second.ShouldNotBeSameAs(first);
    }
}