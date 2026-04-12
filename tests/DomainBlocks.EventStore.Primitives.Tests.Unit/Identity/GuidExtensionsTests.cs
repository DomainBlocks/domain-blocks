using DomainBlocks.EventStore.Primitives.Identity;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Primitives.Tests.Unit.Identity;

public class GuidExtensionsTests
{
    private static readonly Guid DnsNamespace = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    [TestCase("www.widgets.com", "21f7f8de-8051-5b89-8680-0195ef798b6a")]
    [TestCase("www.example.com", "2ed6657d-e927-568b-95e1-2665a8aea6a2")]
    public void CreateVersion5_WithRfc4122TestVectors_MatchesExpectedGuids(string name, string expected)
    {
        var actual = Guid.CreateVersion5(DnsNamespace, name);
        actual.ToString().ShouldBe(expected);
    }
}