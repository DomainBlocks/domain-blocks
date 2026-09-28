using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class FilterOptionsTests
{
    [Test]
    public void Defaults_WhenOptionsAreCreated_UseAllFilterAndPreferPushdown()
    {
        ReadAllOptions.Default.Filter.ShouldBe(EventFilter.All);
        ReadStreamOptions.Default.Filter.ShouldBe(EventFilter.All);
        SubscriptionOptions.Default.Filter.ShouldBe(EventFilter.All);

        ReadAllOptions.Default.FilterPushdownMode.ShouldBe(FilterPushdownMode.Prefer);
        ReadStreamOptions.Default.FilterPushdownMode.ShouldBe(FilterPushdownMode.Prefer);
        SubscriptionOptions.Default.FilterPushdownMode.ShouldBe(FilterPushdownMode.Prefer);
    }

    [Test]
    public void Filter_WhenSetToNull_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new ReadAllOptions { Filter = null! });
        Should.Throw<ArgumentNullException>(() => new ReadStreamOptions { Filter = null! });
        Should.Throw<ArgumentNullException>(() => new SubscriptionOptions { Filter = null! });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void MaxCount_WhenSetToZeroOrNegative_ThrowsArgumentOutOfRangeException(int maxCount)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ReadAllOptions { MaxCount = maxCount });
        Should.Throw<ArgumentOutOfRangeException>(() => new ReadStreamOptions { MaxCount = maxCount });

        new ReadAllOptions { MaxCount = null }.MaxCount.ShouldBeNull();
        new ReadStreamOptions { MaxCount = 1 }.MaxCount.ShouldBe(1);
    }

    [Test]
    public void ThrowIfFiltered_WhenNoFilterIsGiven_DoesNotThrow()
    {
        Should.NotThrow(() => EventFilterNotSupportedException.ThrowIfFiltered(null, "A store"));
        Should.NotThrow(() => EventFilterNotSupportedException.ThrowIfFiltered(EventFilter.All, "A store"));
    }

    [Test]
    public void ThrowIfFiltered_WhenUnsupportedFilterIsGiven_IdentifiesTheStoreAndFilter()
    {
        var exception = Should.Throw<EventFilterNotSupportedException>(() =>
            EventFilterNotSupportedException.ThrowIfFiltered(EventFilter.StreamIds("s"), "A store"));

        exception.Message.ShouldBe("""'A store' does not support event filters, but was given 'streamId("s")'.""");
    }
}