using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.TypeMapping;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

public sealed record OrderPlaced
{
    public required int Number { get; init; }
}

public sealed record OrderShipped
{
    public required int Number { get; init; }
}

public sealed record InvoiceRaised
{
    public required int Number { get; init; }
}

/// <summary>
/// An event of the test log as it was read without a filter. The expectation of each case is written against it by
/// hand, and it is the stored form that a filter is evaluated against in memory. Its index is its place in the log,
/// counting from zero.
/// </summary>
public sealed record LoggedEvent(
    int Index,
    string EventName,
    string StreamId,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt) : IFilterableEvent
{
    public string? Tenant => Metadata.GetValueOrDefault("tenant");

    public bool TryGetMetadata(string key, [MaybeNullWhen(false)] out string value) =>
        Metadata.TryGetValue(key, out value);
}

/// <summary>
/// How much of the test log a case is meant to select. Declared so that a case cannot pass by accident, by selecting
/// nothing or everything whatever a store does with its filter.
/// </summary>
public enum CaseSelection
{
    Some,
    Nothing,
    Everything
}

/// <summary>
/// A filter and the events of the test log that it should select, written by hand rather than derived from the filter.
/// Both are given the midpoint of the log, for the cases that depend on when its events were created.
/// </summary>
public sealed record EventFilterCase(
    string Name,
    Func<DateTimeOffset, EventFilter> Filter,
    Func<DateTimeOffset, LoggedEvent, bool> Expected,
    CaseSelection Selects = CaseSelection.Some)
{
    public EventFilterCase(
        string name,
        EventFilter filter,
        Func<LoggedEvent, bool> expected,
        CaseSelection selects = CaseSelection.Some)
        : this(name, _ => filter, (_, e) => expected(e), selects)
    {
    }

    public override string ToString() => Name;
}

/// <summary>
/// The log that filtered reads and subscriptions are tested against, and the filters they are tested with.
/// </summary>
public static class EventFilterTestLog
{
    public const int Count = 40;

    public const string AwkwardTenant = "o'brien \"and\" sons\\";

    private const string Placed = nameof(OrderPlaced);
    private const string Shipped = nameof(OrderShipped);
    private const string Invoiced = nameof(InvoiceRaised);

    // Stream IDs and a tenant with characters that a query language could take for its own.
    private static readonly string[] OrderStreams = ["order-1", "order-10", "order_2", "order%3"];
    private static readonly string[] InvoiceStreams = ["invoice.1", "invoice-1"];
    private static readonly string[] Tenants = ["acme", "initech", AwkwardTenant];

    /// <summary>
    /// The events before which the appender pauses, so that the log spans several instants whatever the precision of
    /// the store's clock. The first of them is the midpoint that the cases are given.
    /// </summary>
    public static IReadOnlyList<int> PauseBeforeIndexes { get; } = [14, 27];

    public static EventTypeMap TypeMap { get; } = new EventTypeMapBuilder()
        .Add<OrderPlaced>()
        .Add<OrderShipped>()
        .Add<InvoiceRaised>()
        .Build();

    /// <summary>
    /// Forty events of three kinds over six streams. The kind, the stream, and the metadata each cycle at a different
    /// rate, so that every stream has events of every shape of metadata: none, a tenant, a tenant and an empty note, a
    /// region, and a tenant and a region.
    /// </summary>
    public static IEnumerable<(string StreamId, AppendableEvent<object> Event)> Events()
    {
        for (var i = 0; i < Count; i++)
        {
            object payload = (i % 3) switch
            {
                0 => new OrderPlaced { Number = i },
                1 => new OrderShipped { Number = i },
                _ => new InvoiceRaised { Number = i }
            };

            var streamId = payload is InvoiceRaised
                ? InvoiceStreams[i / 3 % InvoiceStreams.Length]
                : OrderStreams[i / 3 % OrderStreams.Length];

            var tenant = Tenants[i / 5 % Tenants.Length];

            KeyValuePair<string, string>[] metadata = (i % 5) switch
            {
                0 => [],
                1 => [new("tenant", tenant)],
                2 => [new("tenant", tenant), new("note", "")],
                3 => [new("region", "eu")],
                _ => [new("tenant", tenant), new("region", "eu")]
            };

            yield return (streamId, AppendableEvent.Create(payload, metadata));
        }
    }

    public static IReadOnlyList<EventFilterCase> Cases { get; } =
    [
        new("All", EventFilter.All, _ => true, CaseSelection.Everything),
        new("None", EventFilter.None, _ => false, CaseSelection.Nothing),

        new("EventNames_One", EventFilter.EventNames(Placed), e => e.EventName == Placed),
        new("EventNames_Several", EventFilter.EventNames(Placed, Invoiced), e => e.EventName is Placed or Invoiced),
        new("EventNames_NotInLog", EventFilter.EventNames("OrderCancelled"), _ => false, CaseSelection.Nothing),
        new("EventNames_DifferingByCase", EventFilter.EventNames("orderplaced"), _ => false, CaseSelection.Nothing),

        new("StreamIds_One", EventFilter.StreamIds("order-1"), e => e.StreamId == "order-1"),
        new(
            "StreamIds_Several",
            EventFilter.StreamIds("order_2", "order%3", "invoice.1"),
            e => e.StreamId is "order_2" or "order%3" or "invoice.1"),
        new("StreamIds_NotInLog", EventFilter.StreamIds("order-2"), _ => false, CaseSelection.Nothing),

        new(
            "StreamIdStartsWith_Hyphen",
            EventFilter.StreamIdStartsWith("order-"),
            e => e.StreamId is "order-1" or "order-10"),
        new("StreamIdStartsWith_Underscore", EventFilter.StreamIdStartsWith("order_"), e => e.StreamId == "order_2"),
        new("StreamIdStartsWith_Percent", EventFilter.StreamIdStartsWith("order%"), e => e.StreamId == "order%3"),
        new("StreamIdStartsWith_Dot", EventFilter.StreamIdStartsWith("invoice."), e => e.StreamId == "invoice.1"),
        new(
            "StreamIdStartsWith_WholeId",
            EventFilter.StreamIdStartsWith("order-1"),
            e => e.StreamId is "order-1" or "order-10"),
        new(
            "StreamIdStartsWith_DifferingByCase",
            EventFilter.StreamIdStartsWith("Order"),
            _ => false,
            CaseSelection.Nothing),

        new("MetadataExists_Key", EventFilter.MetadataExists("tenant"), e => e.Metadata.ContainsKey("tenant")),
        new(
            "MetadataExists_KeyWithEmptyValue",
            EventFilter.MetadataExists("note"),
            e => e.Metadata.ContainsKey("note")),
        new("MetadataExists_NoSuchKey", EventFilter.MetadataExists("Tenant"), _ => false, CaseSelection.Nothing),

        new("Metadata_OneValue", EventFilter.Metadata("tenant", "acme"), e => e.Tenant == "acme"),
        new(
            "Metadata_SeveralValues",
            EventFilter.Metadata("tenant", "acme", "initech"),
            e => e.Tenant is "acme" or "initech"),
        new(
            "Metadata_ValueWithQuotesAndBackslash",
            EventFilter.Metadata("tenant", AwkwardTenant),
            e => e.Tenant == AwkwardTenant),
        new("Metadata_EmptyValue", EventFilter.Metadata("note", ""), e => e.Metadata.GetValueOrDefault("note") == ""),
        new("Metadata_NoSuchValue", EventFilter.Metadata("tenant", "globex"), _ => false, CaseSelection.Nothing),
        new("Metadata_ValueOfAnotherKey", EventFilter.Metadata("region", "acme"), _ => false, CaseSelection.Nothing),

        new("CreatedAtOrAfter_AnEvent", EventFilter.CreatedAtOrAfter, (midpoint, e) => e.CreatedAt >= midpoint),
        new("CreatedBefore_AnEvent", EventFilter.CreatedBefore, (midpoint, e) => e.CreatedAt < midpoint),

        // A bound that falls between the instants that a store can tell apart.
        new(
            "CreatedAtOrAfter_JustAfterAnEvent",
            midpoint => EventFilter.CreatedAtOrAfter(midpoint.AddTicks(1)),
            (midpoint, e) => e.CreatedAt > midpoint),
        new(
            "CreatedBefore_JustAfterAnEvent",
            midpoint => EventFilter.CreatedBefore(midpoint.AddTicks(1)),
            (midpoint, e) => e.CreatedAt <= midpoint),

        new(
            "CreatedAtOrAfter_EarliestInstant",
            EventFilter.CreatedAtOrAfter(DateTimeOffset.MinValue),
            _ => true,
            CaseSelection.Everything),
        new(
            "CreatedBefore_LatestInstant",
            EventFilter.CreatedBefore(DateTimeOffset.MaxValue),
            _ => true,
            CaseSelection.Everything),
        new(
            "CreatedAtOrAfter_LatestInstant",
            EventFilter.CreatedAtOrAfter(DateTimeOffset.MaxValue),
            _ => false,
            CaseSelection.Nothing),

        new(
            "And_NameAndStream",
            EventFilter.EventNames(Placed) & EventFilter.StreamIds("order-1"),
            e => e is { EventName: Placed, StreamId: "order-1" }),
        new(
            "And_NameMetadataAndCreatedAt",
            midpoint =>
                EventFilter.EventNames(Shipped) &
                EventFilter.MetadataExists("tenant") &
                EventFilter.CreatedAtOrAfter(midpoint),
            (midpoint, e) => e.EventName == Shipped && e.Metadata.ContainsKey("tenant") && e.CreatedAt >= midpoint),
        new(
            "And_Contradiction",
            EventFilter.EventNames(Placed) & EventFilter.EventNames(Shipped),
            _ => false,
            CaseSelection.Nothing),

        new(
            "Or_StreamOrMetadata",
            EventFilter.StreamIds("invoice-1") | EventFilter.Metadata("tenant", "initech"),
            e => e.StreamId == "invoice-1" || e.Tenant == "initech"),
        new(
            "Or_Tautology",
            EventFilter.MetadataExists("tenant") | !EventFilter.MetadataExists("tenant"),
            _ => true,
            CaseSelection.Everything),

        new("Not_Name", !EventFilter.EventNames(Placed), e => e.EventName != Placed),
        new(
            "Not_StreamIdStartsWith",
            !EventFilter.StreamIdStartsWith("order"),
            e => !e.StreamId.StartsWith("order", StringComparison.Ordinal)),

        // These also select the events that have no metadata at all.
        new("Not_MetadataExists", !EventFilter.MetadataExists("tenant"), e => !e.Metadata.ContainsKey("tenant")),
        new("Not_MetadataValue", !EventFilter.Metadata("tenant", "acme"), e => e.Tenant != "acme"),

        new(
            "Not_CreatedBefore",
            midpoint => !EventFilter.CreatedBefore(midpoint),
            (midpoint, e) => e.CreatedAt >= midpoint),
        new(
            "Not_And",
            !(EventFilter.EventNames(Placed) & EventFilter.MetadataExists("tenant")),
            e => !(e.EventName == Placed && e.Metadata.ContainsKey("tenant"))),
        new(
            "Not_Or",
            !(EventFilter.StreamIds("order-1") | EventFilter.Metadata("tenant", "acme")),
            e => !(e.StreamId == "order-1" || e.Tenant == "acme")),

        new(
            "And_OrAndNot",
            (EventFilter.EventNames(Placed) | EventFilter.EventNames(Invoiced)) & !EventFilter.Metadata("region", "eu"),
            e => e.EventName is Placed or Invoiced && e.Metadata.GetValueOrDefault("region") != "eu"),
        new(
            "Or_AndWithNot",
            EventFilter.StreamIds("order%3") | (EventFilter.MetadataExists("note") & !EventFilter.EventNames(Shipped)),
            e => e.StreamId == "order%3" || e.Metadata.ContainsKey("note") && e.EventName != Shipped)
    ];
}