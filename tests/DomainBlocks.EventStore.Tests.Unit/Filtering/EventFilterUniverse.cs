using System.Linq.Expressions;
using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

/// <summary>
/// A small, fully enumerable set of events and random filters over it. Two filters are equivalent in this universe when
/// they match the same events.
/// </summary>
internal static class EventFilterUniverse
{
    private static readonly string[] EventNames = ["OrderPlaced", "OrderShipped", "InvoiceRaised"];
    private static readonly string[] StreamIds = ["order-1", "order-2", "invoice-1"];
    private static readonly string[] Tenants = ["acme", "initech"];
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // Deliberately dereferences Customer so the predicate exercises null handling. Keep the expression instance stable
    // because predicate equality is reference-based.
    private static readonly Expression<Func<Order, bool>> IsFromAcme = e => e.Customer!.Name == "acme";

    // A distinct predicate so generated filters can contain different predicate leaves.
    private static readonly Expression<Func<Order, bool>> IsSmall = e => e.Total < 100;

    public static IReadOnlyList<StoredEvent> Events { get; } =
    [
        ..
        from eventName in EventNames
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
                : new Order(tenant is null ? null : new Customer(tenant), hour == 0 ? 0 : 100 + hour)
        }
    ];

    public interface IOrderEvent;

    public sealed record Order(Customer? Customer, int Total) : IOrderEvent;

    public sealed record Invoice;

    public sealed record Customer(string Name);

    /// <summary>
    /// Generates a random filter with at most <paramref name="depth"/> levels of operators.
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

    public static IReadOnlyCollection<string> GetEventNames(Type eventType) =>
    [
        .. EventNames.Where(x => eventType.IsAssignableFrom(x == "InvoiceRaised" ? typeof(Invoice) : typeof(Order)))
    ];

    public static bool AreEquivalent(EventFilter left, EventFilter right) =>
        Events.All(x => left.Matches(x) == right.Matches(x));

    private static EventFilter NextLeaf(Random random)
    {
        return random.Next(13) switch
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
            11 => EventFilter.OfType(IsSmall),
            _ => EventFilter.CreatedBefore(Noon.AddHours(random.Next(-1, 2)))
        };

        static string[] Some(Random random, string[] values) =>
            [.. values.Where(_ => random.Next(2) == 0).DefaultIfEmpty(values[0])];
    }
}