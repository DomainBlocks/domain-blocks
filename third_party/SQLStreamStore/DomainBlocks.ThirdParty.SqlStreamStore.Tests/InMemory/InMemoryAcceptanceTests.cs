 // ReSharper disable once CheckNamespace

 using DomainBlocks.ThirdParty.SqlStreamStore.InMemory;

 namespace SqlStreamStore
{
    using System.Threading.Tasks;
    using Xunit.Abstractions;

    public class InMemoryAcceptanceTests : AcceptanceTests
    {
        public InMemoryAcceptanceTests(ITestOutputHelper testOutputHelper)
            : base(testOutputHelper)
        { }

        protected override Task<IStreamStoreFixture> CreateFixture() 
            => Task.FromResult<IStreamStoreFixture>(new InMemoryStreamStoreFixture());
    }
}