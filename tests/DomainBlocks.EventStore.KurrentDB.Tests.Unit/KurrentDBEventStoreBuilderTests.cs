using KurrentDB.Client;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Unit;

/// <summary>
/// The builder's validation, which needs no server: nothing in Build touches it.
/// </summary>
public class KurrentDBEventStoreBuilderTests
{
    private const string ConnectionString = "esdb://localhost:2113?tls=false";

    private sealed record OrderPlaced(string OrderId);

    [Test]
    public void Build_WithoutConnection_ThrowsNamingTheMethodsToCall()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            new KurrentDBEventStoreBuilder<object>().MapEvent<OrderPlaced>().Build());

        ex.Message.ShouldContain("UseClient");
        ex.Message.ShouldContain("UseConnectionString");
    }

    [Test]
    public void Build_WithoutMappings_ThrowsNamingMapEvents()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            new KurrentDBEventStoreBuilder<object>().UseConnectionString(ConnectionString).Build());

        ex.Message.ShouldContain("MapEvents");
    }

    [Test]
    public async Task UseClient_AfterUseConnectionString_Throws()
    {
        await using var client = new KurrentDBClient(KurrentDBClientSettings.Create(ConnectionString));
        var builder = new KurrentDBEventStoreBuilder<object>().UseConnectionString(ConnectionString);

        Should.Throw<InvalidOperationException>(() => builder.UseClient(client));
    }

    [Test]
    public async Task Build_BorrowedClient_CanBuildMoreThanOneStore()
    {
        await using var client = new KurrentDBClient(KurrentDBClientSettings.Create(ConnectionString));
        var builder = new KurrentDBEventStoreBuilder<object>().UseClient(client).MapEvent<OrderPlaced>();

        await using var first = builder.Build();
        await using var second = builder.Build();

        second.ShouldNotBeSameAs(first);
    }

    [Test]
    public async Task EnsureInitializedAsync_CompletesWithoutTouchingTheServer()
    {
        await using var store = new KurrentDBEventStoreBuilder<object>()
            .UseConnectionString(ConnectionString)
            .MapEvent<OrderPlaced>()
            .Build();

        var initialized = store.EnsureInitializedAsync();

        initialized.IsCompletedSuccessfully.ShouldBeTrue();
        await initialized;
    }

    [Test]
    public async Task Build_OwnedClient_BuildsOnceThenThrows()
    {
        var builder = new KurrentDBEventStoreBuilder<object>()
            .UseConnectionString(ConnectionString)
            .MapEvent<OrderPlaced>();

        await using var first = builder.Build();

        Should.Throw<InvalidOperationException>(builder.Build).Message.ShouldContain("one store");
    }
}