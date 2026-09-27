using System.Linq.Expressions;
using System.Reflection;
using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

/// <summary>
/// A small world of events, and random filters over it. The world is small enough to list in full, so two filters
/// match the same events if they agree on every event in it.
/// </summary>
internal static class EventFilterUniverse
{
    private static readonly string[] EventNames = ["OrderPlaced", "OrderShipped", "InvoiceRaised"];
    private static readonly string[] StreamIds = ["order-1", "order-2", "invoice-1"];
    private static readonly string[] Tenants = ["acme", "initech"];
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // Meets a null where an order has no customer. Held once, as predicates are equal only if they are the same one.
    private static readonly Expression<Func<Order, bool>> IsFromAcme = e => e.Customer!.Name == "acme";

    // Over a member that is not always stored as what it is read as.
    private static readonly Expression<Func<Order, bool>> IsSmall = e => e.Total < 100;
    private static readonly Expression<Func<Order, bool>> IsNotLarge = e => !(e.Total > 100) || e.Customer == null;

    // True of an order that was stored before orders had totals, which has no total for a database to compare.
    private static readonly Expression<Func<Order, bool>> HasNoTotal = e => e.Total == 0;
    private static readonly Expression<Func<Order, bool>> IsNotOneOver = e => e.Total != 101;

    private static readonly Expression<Func<Order, bool>> IsLargeWithACustomer =
        e => e.Total >= 100 && e.Customer != null;

    private static readonly Expression<Func<Order, bool>> IsNotOneUnder = e => !(e.Total == 99);

    public static IReadOnlyList<StoredEvent> Events { get; } =
    [
        .. from eventName in EventNames
        from streamId in StreamIds
        from tenant in (string?[])[null, .. Tenants]
        from hour in (int[])[-1, 0, 1]
        select new StoredEvent
        {
            EventName = eventName,
            StreamId = streamId,
            CreatedAt = Noon.AddHours(hour),
            Metadata = tenant is null ? [] : new Dictionary<string, string> { ["tenant"] = tenant },
            DecodedPayload = eventName == "InvoiceRaised"
                ? new Invoice()
                : new Order(tenant is null ? null : new Customer(tenant), hour == 0 ? 0 : 100 + hour),

            // Invoices are stored as bytes, and orders as JSON, with a total that goes by the hour. An order at noon
            // was stored before orders had totals, and no customer was ever stored with its name.
            StoredPayload = eventName == "InvoiceRaised"
                ? null
                : $$"""{ {{JsonTotal(hour)}}"tags": ["{{streamId}}", {{hour}}], "customer": {{Json(tenant)}} }"""
        }
    ];

    private static string Json(string? tenant) => tenant is null ? "null" : "{}";

    private static string JsonTotal(int hour) => hour == 0 ? "" : $"\"total\": {100 + hour}, ";

    public interface IOrderEvent;

    public sealed record Order(Customer? Customer, int Total) : IOrderEvent;

    public sealed record Invoice;

    public sealed record Customer(string Name);

    /// <summary>
    /// A filter of up to <paramref name="depth"/> levels of operators over random leaves.
    /// </summary>
    public static EventFilter NextFilter(Random random, int depth = 3)
    {
        if (depth == 0 || random.Next(3) == 0)
            return NextLeaf(random);

        return random.Next(3) switch
        {
            0 => NextFilter(random, depth - 1) & NextFilter(random, depth - 1),
            1 => NextFilter(random, depth - 1) | NextFilter(random, depth - 1),
            _ => !NextFilter(random, depth - 1)
        };
    }

    /// <summary>
    /// The names that are read as a type, as a codec would answer.
    /// </summary>
    public static IReadOnlyCollection<string> GetEventNames(Type eventType) =>
        [.. EventNames.Where(x => eventType.IsAssignableFrom(x == "InvoiceRaised" ? typeof(Invoice) : typeof(Order)))];

    /// <summary>
    /// Where a member of an order is stored, as a codec would answer: under its name in lower case.
    /// </summary>
    public static string? GetStoredPath(Type eventType, IReadOnlyList<MemberInfo> members) =>
        eventType == typeof(Order) ? string.Join('.', members.Select(x => x.Name.ToLowerInvariant())) : null;

    public static bool AreEquivalent(EventFilter left, EventFilter right) =>
        Events.All(x => left.Matches(x) == right.Matches(x));

    private static EventFilter NextLeaf(Random random)
    {
        return random.Next(18) switch
        {
            0 => EventFilter.All,
            1 => EventFilter.None,
            2 => EventFilter.EventNames(Some(random, EventNames)),
            3 => EventFilter.StreamIds(Some(random, StreamIds)),
            4 => EventFilter.StreamIdStartsWith(random.Next(2) == 0 ? "order-" : "invoice-"),
            5 => EventFilter.MetadataExists("tenant"),
            6 => EventFilter.Metadata("tenant", Some(random, Tenants)),
            7 => EventFilter.CreatedAtOrAfter(Noon.AddHours(random.Next(-1, 2))),
            8 => EventFilter.OfType<IOrderEvent>(),
            9 => EventFilter.OfType<Invoice>(),
            10 => EventFilter.OfType(IsFromAcme),
            11 => EventFilter.OfType(HasNoTotal),
            12 => EventFilter.OfType(IsNotOneOver),
            13 => EventFilter.OfType(IsLargeWithACustomer),
            14 => EventFilter.OfType(IsNotOneUnder),
            15 => EventFilter.OfType(IsSmall),
            16 => EventFilter.OfType(IsNotLarge),
            _ => EventFilter.CreatedBefore(Noon.AddHours(random.Next(-1, 2)))
        };
    }

    private static string[] Some(Random random, string[] values) =>
        [.. values.Where(_ => random.Next(2) == 0).DefaultIfEmpty(values[0])];
}