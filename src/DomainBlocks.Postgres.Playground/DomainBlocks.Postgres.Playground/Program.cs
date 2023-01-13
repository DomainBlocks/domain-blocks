namespace DomainBlocks.Postgres.Playground;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        await TestLogicalReplication(args);
    }

    // See https://github.com/oskardudycz/PostgresOutboxPatternWithCDC.NET
    private static async Task TestLogicalReplication(string[] args)
    {
        const string connectionString =
            "Server=localhost;Port=5433;Database=shopping;User Id=postgres;Password=postgres;";

        var subscriptionOptions = new EventsSubscriptionOptions(connectionString, args[0], "messages_pub");
        var subscription = new EventsSubscription();

        await foreach (var @event in subscription.Subscribe(subscriptionOptions, default))
        {
            Console.WriteLine(@event);
        }
    }
}