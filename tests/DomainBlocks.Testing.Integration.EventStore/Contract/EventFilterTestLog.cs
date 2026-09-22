using System.Linq.Expressions;
using System.Text.Json;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.TypeMapping;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

public interface IOrderEvent;

public sealed record OrderPlaced : IOrderEvent
{
    public required int Total { get; init; }

    public OrderCustomer? Customer { get; init; }

    public OrderLine[] Lines { get; init; } = [];
}

public sealed record OrderLine
{
    public required string Sku { get; init; }
}

public sealed record OrderCustomer
{
    public required string Name { get; init; }
}

public sealed record OrderShipped : IOrderEvent
{
    public required string Carrier { get; init; }

    public int[] Parcels { get; init; } = [];
}

public sealed record InvoiceRaised
{
    public required int Amount { get; init; }
}

/// <summary>
/// Stored like any other event, and ignored by the store that reads the log.
/// </summary>
public sealed record OrderRetired
{
    public required string Reason { get; init; }
}

/// <summary>
/// Appended last by a test that waits for live events, so that it knows when it has seen them all.
/// </summary>
public sealed record FilterTestMarker
{
    public required string Id { get; init; }
}

/// <summary>
/// An event of the log as a filter sees it, for saying by hand what a filter should select.
/// </summary>
/// <param name="LogPosition">Where it is in the log, which is what tells one event from another.</param>
/// <param name="Payload">The event as the store under test reads it, which for an ignored name is the sentinel.</param>
/// <param name="EventName">The name it is stored under.</param>
/// <param name="StreamId">The stream it is in.</param>
/// <param name="Metadata">Its metadata as it is stored, which for an ignored name is not what is read.</param>
/// <param name="CreatedAt">When it was stored.</param>
public sealed record LoggedEvent(
    object LogPosition,
    object Payload,
    string EventName,
    string StreamId,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt)
{
    public string? Tenant => Metadata.GetValueOrDefault("tenant");
}

/// <summary>
/// A filter, and what it should select, said by hand rather than by the filter.
/// </summary>
public sealed record EventFilterCase(
    string Name,
    Func<IReadOnlyList<LoggedEvent>, EventFilter> Filter,
    Func<IReadOnlyList<LoggedEvent>, LoggedEvent, bool> Expected)
{
    public EventFilterCase(string name, EventFilter filter, Func<LoggedEvent, bool> expected)
        : this(name, _ => filter, (_, e) => expected(e))
    {
    }

    public override string ToString() => Name;
}

/// <summary>
/// The log that filters are tested against, and the filters.
/// </summary>
public static class EventFilterTestLog
{
    public const string IgnoredEventName = nameof(OrderRetired);

    // Held once each, as predicates are equal only if they are the same one.
    private static readonly Expression<Func<OrderPlaced, bool>> IsLarge = e => e.Total >= 100;
    private static readonly Expression<Func<OrderPlaced, bool>> IsFromAnn = e => e.Customer!.Name == "Ann";

    public static EventTypeMap TypeMap { get; } = EventTypeMap.Create(
        EventTypeMapping.ReadWrite<OrderPlaced>(),
        EventTypeMapping.ReadWrite<OrderShipped>(),
        EventTypeMapping.ReadWrite<InvoiceRaised>(),
        EventTypeMapping.ReadWrite<OrderRetired>());

    /// <summary>
    /// Forty events over six streams. Every fourth is of a kind, every third has no tenant, and one order in two has no
    /// customer. Stream ids and a tenant have characters that a query language might take for its own.
    /// </summary>
    public static IEnumerable<(string StreamId, AppendableEvent<object> Event)> Events()
    {
        string[] orderStreams = ["order-1", "order-10", "order_2", "order%3"];
        string[] tenants = ["acme", "initech", "o'brien \"and\" sons\\"];

        for (var i = 0; i < 40; i++)
        {
            object payload = (i % 4) switch
            {
                0 => new OrderPlaced
                {
                    Total = i * 5,
                    Customer = i % 8 == 0 ? null : new OrderCustomer { Name = i % 3 == 0 ? "Ann" : "Bob" },
                    Lines = [new OrderLine { Sku = $"sku-{i}" }, new OrderLine { Sku = $"sku-{i + 1}" }]
                },
                1 => new OrderShipped { Carrier = $"carrier-{i}", Parcels = [i, i + 4] },
                2 => new InvoiceRaised { Amount = i },
                _ => new OrderRetired { Reason = $"reason-{i}" }
            };

            var streamId = payload is InvoiceRaised ? $"invoice-{i % 2}" : orderStreams[i / 4 % orderStreams.Length];

            List<KeyValuePair<string, string>> metadata = [];

            if (i % 3 != 0)
                metadata.Add(new("tenant", tenants[i / 3 % tenants.Length]));

            if (i % 5 == 0)
                metadata.Add(new("user", $"user-{i}"));

            yield return (streamId, AppendableEvent.Create(payload, metadata));
        }
    }

    public static IReadOnlyList<EventFilterCase> Cases { get; } =
    [
        new("All", EventFilter.All, _ => true),
        new("None", EventFilter.None, _ => false),

        new("EventName", EventFilter.EventName(nameof(OrderPlaced)), e => e.EventName == nameof(OrderPlaced)),
        new("EventNames",
            EventFilter.EventNames(nameof(OrderPlaced), nameof(InvoiceRaised)),
            e => e.EventName is nameof(OrderPlaced) or nameof(InvoiceRaised)),
        new("EventName_NotInTheLog", EventFilter.EventName("Nope"), _ => false),
        new("EventName_Ignored", EventFilter.EventName(IgnoredEventName), e => e.EventName == IgnoredEventName),

        new("StreamId", EventFilter.StreamId("order-1"), e => e.StreamId == "order-1"),
        new("StreamIds", EventFilter.StreamIds("order-10", "invoice-0"), e => e.StreamId is "order-10" or "invoice-0"),
        new("StreamIdStartsWith", EventFilter.StreamIdStartsWith("order-"), e => e.StreamId is "order-1" or "order-10"),
        new("StreamIdStartsWith_WholeId",
            EventFilter.StreamIdStartsWith("order-1"),
            e => e.StreamId is "order-1" or "order-10"),
        new("StreamIdStartsWith_Underscore", EventFilter.StreamIdStartsWith("order_"), e => e.StreamId == "order_2"),
        new("StreamIdStartsWith_Percent", EventFilter.StreamIdStartsWith("order%"), e => e.StreamId == "order%3"),

        new("MetadataExists", EventFilter.MetadataExists("tenant"), e => e.Tenant is not null),
        new("MetadataExists_Not", !EventFilter.MetadataExists("tenant"), e => e.Tenant is null),
        new("Metadata", EventFilter.Metadata("tenant", "acme"), e => e.Tenant == "acme"),
        new("Metadata_Several", EventFilter.Metadata("tenant", "acme", "initech"), e => e.Tenant is "acme" or "initech"),
        new("Metadata_AwkwardValue",
            EventFilter.Metadata("tenant", "o'brien \"and\" sons\\"),
            e => e.Tenant == "o'brien \"and\" sons\\"),
        new("Metadata_NoSuchKey", EventFilter.Metadata("nope", "acme"), _ => false),

        // An event without the entry does not match the filter, so it matches the negation.
        new("Metadata_Not", !EventFilter.Metadata("tenant", "acme"), e => e.Tenant != "acme"),

        new("CreatedAtOrAfter",
            log => EventFilter.CreatedAtOrAfter(Middle(log)),
            (log, e) => e.CreatedAt >= Middle(log)),
        new("CreatedBefore", log => EventFilter.CreatedBefore(Middle(log)), (log, e) => e.CreatedAt < Middle(log)),
        new("CreatedAt_Between",
            log => EventFilter.CreatedAtOrAfter(log[5].CreatedAt) & EventFilter.CreatedBefore(Middle(log)),
            (log, e) => e.CreatedAt >= log[5].CreatedAt && e.CreatedAt < Middle(log)),
        new("CreatedAt_Not", log => !EventFilter.CreatedBefore(Middle(log)), (log, e) => e.CreatedAt >= Middle(log)),

        new("OfType", EventFilter.OfType<OrderPlaced>(), e => e.Payload is OrderPlaced),
        new("OfType_Interface", EventFilter.OfType<IOrderEvent>(), e => e.Payload is IOrderEvent),
        new("OfType_Everything", EventFilter.OfType<object>(), _ => true),
        new("OfType_Ignored", EventFilter.OfType<IgnoredEvent>(), e => e.Payload is IgnoredEvent),
        new("OfType_NotMapped", EventFilter.OfType<string>(), _ => false),
        new("OfType_Not", !EventFilter.OfType<IOrderEvent>(), e => e.Payload is not IOrderEvent),

        new("Predicate", EventFilter.OfType(IsLarge), e => e.Payload is OrderPlaced { Total: >= 100 }),
        new("Predicate_MeetsNull",
            EventFilter.OfType(IsFromAnn),
            e => e.Payload is OrderPlaced { Customer.Name: "Ann" }),
        new("Predicate_Not", !EventFilter.OfType(IsLarge), e => e.Payload is not OrderPlaced { Total: >= 100 }),

        new("And_Mixed",
            EventFilter.StreamIdStartsWith("order") &
            EventFilter.MetadataExists("tenant") &
            (EventFilter.OfType(IsLarge) | EventFilter.OfType<OrderShipped>()),
            e => e.StreamId.StartsWith("order", StringComparison.Ordinal) &&
                 e.Tenant is not null &&
                 e.Payload is OrderPlaced { Total: >= 100 } or OrderShipped),

        new("Or_Mixed",
            EventFilter.EventName(nameof(InvoiceRaised)) |
            (EventFilter.StreamId("order-1") & !EventFilter.Metadata("tenant", "acme")),
            e => e.EventName == nameof(InvoiceRaised) || e is { StreamId: "order-1", Tenant: not "acme" }),

        new("Or_PredicateAndMetadata",
            EventFilter.OfType(IsFromAnn) | EventFilter.Metadata("tenant", "initech"),
            e => e.Payload is OrderPlaced { Customer.Name: "Ann" } || e.Tenant == "initech"),

        new("Not_OverOr",
            !(EventFilter.EventName(nameof(OrderPlaced)) | EventFilter.MetadataExists("user")),
            e => e.EventName != nameof(OrderPlaced) && !e.Metadata.ContainsKey("user")),

        new("Not_OverAndWithPredicate",
            !(EventFilter.StreamId("order-10") & EventFilter.OfType(IsLarge)),
            e => !(e.StreamId == "order-10" && e.Payload is OrderPlaced { Total: >= 100 }))
    ];

    /// <summary>
    /// An event as text, for comparing what was read with what was expected. The events have arrays, which a record
    /// compares by reference, so an event that was read is not equal to the one that was appended.
    /// </summary>
    public static string Describe(object payload) =>
        $"{payload.GetType().Name} {JsonSerializer.Serialize(payload, payload.GetType())}";

    private static DateTimeOffset Middle(IReadOnlyList<LoggedEvent> log) => log[log.Count / 2].CreatedAt;
}