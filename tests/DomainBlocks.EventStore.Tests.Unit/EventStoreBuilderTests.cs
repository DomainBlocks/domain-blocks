using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Tests.Unit.Codecs;
using DomainBlocks.EventStore.Tests.Unit.Decoration;
using DomainBlocks.EventStore.Transforms;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class EventStoreBuilderTests
{
    [Test]
    public void SharedMethods_ReturnTheDerivingBuilder_SoBackendMethodsStillChain()
    {
        var builder = new FakeEventStoreBuilder();

        var chained = builder
            .ConfigureCodec(x => x.MapEvent<Current>())
            .AddMetadataContributors(new UserContributor())
            .AddReadTransform((Legacy e) => new Current(e.Value))
            .UseIgnoredEventSentinel(IgnoredEvent.Instance)
            .UseStore(new FakeEventStore());

        chained.ShouldBeSameAs(builder);
    }

    [Test]
    public void Build_NothingToDecorateWith_ReturnsTheStoreItself()
    {
        var inner = new FakeEventStore();

        var store = new FakeEventStoreBuilder().UseStore(inner).Build();

        store.ShouldBeSameAs(inner);
    }

    [Test]
    public async Task Build_AppliesContributorsAndTransforms()
    {
        var inner = new FakeEventStore();
        inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a"), 0));
        inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Retired(), 1));

        var store = new FakeEventStoreBuilder()
            .UseStore(inner)
            .AddMetadataContributors(new UserContributor())
            .AddReadTransform<Legacy>(e => new Current(e.Value))
            .AddReadTransforms(ReadEventTransform.Create<object, Retired>(_ => []))
            .UseIgnoredEventSentinel(IgnoredEvent.Instance)
            .Build();

        await store.AppendAsync("s", [new Current("x")]);
        var events = await store.ReadStream("s").ToArrayAsync();

        inner.Appends
            .ShouldHaveSingleItem()
            .Events
            .ShouldHaveSingleItem()
            .Metadata
            .ShouldBe([new KeyValuePair<string, string>("user", "bob")]);

        events.Select(x => x.Payload).ShouldBe([new Current("a"), IgnoredEvent.Instance]);
    }

    [Test]
    public async Task AddReadTransform_EveryFuncShape_IsApplied()
    {
        var inner = new FakeEventStore();
        inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new LegacyV1("a"), 0));
        inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new LegacyV2("b"), 1));
        inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new LegacyV3("c"), 2));
        inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new LegacyV4("d"), 3));

        var store = new FakeEventStoreBuilder()
            .UseStore(inner)
            .AddReadTransform((LegacyV1 e) => new Current(e.Value))
            .AddReadTransform((LegacyV2 e, ReadEventInfo _) => new Current(e.Value))
            .AddReadTransform((LegacyV3 e) => [new Current(e.Value)])
            .AddReadTransform<LegacyV4>((e, _) => [new Current(e.Value)])
            .Build();

        var events = await store.ReadStream("s").ToArrayAsync();

        events
            .Select(x => x.Payload)
            .ShouldBe([new Current("a"), new Current("b"), new Current("c"), new Current("d")]);
    }

    [Test]
    public void AddReadTransform_TwoFuncsForOneSourceType_BuildThrows()
    {
        var builder = new FakeEventStoreBuilder()
            .UseStore(new FakeEventStore())
            .AddReadTransform((Legacy e) => new Current(e.Value))
            .AddReadTransform((Legacy e) => new Current(e.Value));

        Should.Throw<ArgumentException>(builder.Build).Message.ShouldContain(nameof(Legacy));
    }

    [Test]
    public void IgnoreEvents_WithUseIgnoredEventSentinel_DecodeUsesSentinel()
    {
        var codec = new FakeEventStoreBuilder()
            .ConfigureCodec(x => x.MapEvent<Current>())
            .IgnoreEvents("Retired")
            .UseIgnoredEventSentinel(IgnoredEvent.Instance)
            .BuildCodecWithFakes();

        codec.Decode("Retired", "anything", null).Payload.ShouldBeSameAs(IgnoredEvent.Instance);
    }

    [Test]
    public void IgnoreEvents_WithoutSentinel_BuildCodecThrows()
    {
        var builder = new FakeEventStoreBuilder()
            .ConfigureCodec(x => x.MapEvent<Current>())
            .IgnoreEvents("Retired");

        Should
            .Throw<InvalidOperationException>(builder.BuildCodecWithFakes)
            .Message
            .ShouldContain("UseIgnoredEventSentinel");
    }

    [Test]
    public void BuildCodec_NoSerializerConfigured_UsesTheGivenDefaults()
    {
        var builder = new FakeEventStoreBuilder().ConfigureCodec(x => x.MapEvent<Current>());

        Should.Throw<DefaultSerializerException>(builder.BuildCodecWithThrowingDefaults);
    }

    private sealed record Legacy(string Value);

    private sealed record LegacyV1(string Value);

    private sealed record LegacyV2(string Value);

    private sealed record LegacyV3(string Value);

    private sealed record LegacyV4(string Value);

    private sealed record Current(string Value);

    private sealed record Retired;

    private sealed class DefaultSerializerException : Exception;

    private sealed class UserContributor : IMetadataContributor<object>
    {
        public void Contribute(object @event, MetadataWriter metadata) => metadata.Set("user", "bob");
    }

    private sealed class FakeEventStoreBuilder : EventStoreBuilder<object, string, string, FakeEventStoreBuilder>
    {
        private FakeEventStore? _store;

        public FakeEventStoreBuilder UseStore(FakeEventStore store)
        {
            _store = store;
            return this;
        }

        public IEventStore<object, string, StreamPosition, LogPosition> Build() => Decorate(_store!);

        public IEventCodec<object, string, string> BuildCodecWithFakes() =>
            BuildCodec(() => new FakeObjectSerializer(), () => new FakeMetadataSerializer());

        public void BuildCodecWithThrowingDefaults()
        {
            BuildCodec(
                () => throw new DefaultSerializerException(),
                () => throw new DefaultSerializerException());
        }
    }
}