using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Unit;

public class KurrentDBEventStoreBuilderTests
{
    [Test]
    public void Build_WithoutConnection_ThrowsNamingTheMethodsToCall()
    {
        var ex = Should.Throw<InvalidOperationException>(() => new KurrentDBEventStoreBuilder<object>().Build());

        ex.Message.ShouldContain("UseClient");
        ex.Message.ShouldContain("UseConnectionString");
    }
}