using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class FilterOptionsTests
{
    private static readonly EventFilter Filter = EventFilter.EventNames("OrderPlaced");

    [Test]
    public void Filter_ByDefault_IsAll()
    {
        new ReadAllOptions().Filter.ShouldBeSameAs(EventFilter.All);
        new ReadStreamOptions().Filter.ShouldBeSameAs(EventFilter.All);
        new SubscriptionOptions().Filter.ShouldBeSameAs(EventFilter.All);
    }

    [Test]
    public void Filter_WhenSet_IsKept()
    {
        new ReadAllOptions { Filter = Filter }.Filter.ShouldBeSameAs(Filter);
        new ReadStreamOptions { Filter = Filter }.Filter.ShouldBeSameAs(Filter);
        new SubscriptionOptions { Filter = Filter }.Filter.ShouldBeSameAs(Filter);
    }

    [Test]
    public void Filter_WhenSetToNull_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new ReadAllOptions { Filter = null! });
        Should.Throw<ArgumentNullException>(() => new ReadStreamOptions { Filter = null! });
        Should.Throw<ArgumentNullException>(() => new SubscriptionOptions { Filter = null! });
    }

    [Test]
    public void ThrowIfFiltered_NoFilterOrAll_DoesNotThrow()
    {
        Should.NotThrow(() => EventFilterNotSupportedException.ThrowIfFiltered(null, "Store.ReadAll"));
        Should.NotThrow(() => EventFilterNotSupportedException.ThrowIfFiltered(EventFilter.All, "Store.ReadAll"));
    }

    [Test]
    public void ThrowIfFiltered_AnyOtherFilter_ThrowsNamingOperationAndFilter()
    {
        var exception = Should.Throw<EventFilterNotSupportedException>(() =>
            EventFilterNotSupportedException.ThrowIfFiltered(Filter, "Store.ReadAll"));

        exception.Message.ShouldBe(
            "Store.ReadAll does not support event filters, but was given the filter EventNames(\"OrderPlaced\").");
    }

    [Test]
    public void ThrowIfFiltered_None_Throws()
    {
        Should.Throw<EventFilterNotSupportedException>(() =>
            EventFilterNotSupportedException.ThrowIfFiltered(EventFilter.None, "Store.ReadAll"));
    }
}