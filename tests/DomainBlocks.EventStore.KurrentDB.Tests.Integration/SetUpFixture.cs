using DomainBlocks.Testing.Integration.KurrentDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[SetUpFixture]
public class SetUpFixture
{
    [OneTimeSetUp]
    public Task OneTimeSetUp() => KurrentDBTestEnvironment.StartAsync();

    [OneTimeTearDown]
    public Task OneTimeTearDown() => KurrentDBTestEnvironment.StopAsync();
}