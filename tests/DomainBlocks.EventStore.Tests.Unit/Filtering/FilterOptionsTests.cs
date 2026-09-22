using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class FilterOptionsTests
{
    [Test]
    public void Filter_WhenNotSet_IsAllWithPushdownPreferred()
    {
        ReadAllOptions.Default.Filter.ShouldBe(EventFilter.All);
        ReadStreamOptions.Default.Filter.ShouldBe(EventFilter.All);
        SubscriptionOptions.Default.Filter.ShouldBe(EventFilter.All);

        ReadAllOptions.Default.FilterPushdownMode.ShouldBe(FilterPushdownMode.Prefer);
        ReadStreamOptions.Default.FilterPushdownMode.ShouldBe(FilterPushdownMode.Prefer);
        SubscriptionOptions.Default.FilterPushdownMode.ShouldBe(FilterPushdownMode.Prefer);
    }

    [Test]
    public void Filter_WhenSetToNull_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new ReadAllOptions { Filter = null! });
        Should.Throw<ArgumentNullException>(() => new ReadStreamOptions { Filter = null! });
        Should.Throw<ArgumentNullException>(() => new SubscriptionOptions { Filter = null! });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void MaxCount_WhenNotPositive_Throws(int maxCount)
    {
        // A store would take a count of nothing for no count at all, and read everything.
        Should.Throw<ArgumentOutOfRangeException>(() => new ReadAllOptions { MaxCount = maxCount });
        Should.Throw<ArgumentOutOfRangeException>(() => new ReadStreamOptions { MaxCount = maxCount });

        new ReadAllOptions { MaxCount = null }.MaxCount.ShouldBeNull();
        new ReadStreamOptions { MaxCount = 1 }.MaxCount.ShouldBe(1);
    }

    [Test]
    public void Options_WhenCopiedWithAnotherFilter_KeepTheRest()
    {
        var options = new ReadStreamOptions { MaxCount = 5, IncludeMetadata = false };

        var copy = options with { Filter = EventFilter.StreamId("s") };

        copy.MaxCount.ShouldBe(5);
        copy.IncludeMetadata.ShouldBeFalse();
        copy.Filter.ShouldBe(EventFilter.StreamId("s"));
    }

    [Test]
    public void ThrowIfFiltered_WhenThereIsNoFilter_DoesNotThrow()
    {
        Should.NotThrow(() => EventFilterNotSupportedException.ThrowIfFiltered(null, "A store"));
        Should.NotThrow(() => EventFilterNotSupportedException.ThrowIfFiltered(EventFilter.All, "A store"));
    }

    [Test]
    public void ThrowIfFiltered_WhenThereIsAFilter_SaysWhatRefusedWhich()
    {
        var exception = Should.Throw<EventFilterNotSupportedException>(
            () => EventFilterNotSupportedException.ThrowIfFiltered(EventFilter.StreamId("s"), "A store"));

        exception.Message.ShouldBe("""A store does not support event filters, and was given 'streamId("s")'.""");
    }
}