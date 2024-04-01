namespace DomainBlocks.V1.Tests.Integration;

public static class ConnectionStrings
{
    public const string PostgresStreamStore =
        "Server=localhost;Port=5434;Database=test-events;User Id=postgres;Password=postgres;";

    public const string EventStoreDb = "esdb://localhost:2114?tls=false";
}