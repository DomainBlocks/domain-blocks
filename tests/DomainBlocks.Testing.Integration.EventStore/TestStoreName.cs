namespace DomainBlocks.Testing.Integration.EventStore;

/// <summary>
/// Names a fixture's schema or database after the fixture, so that fixtures in one assembly never share a store and
/// nobody has to invent names.
/// </summary>
public static class TestStoreName
{
    /// <summary>
    /// PostgreSQL identifiers and MongoDB database names are limited to 63 characters, and the PostgreSQL publication
    /// name appends "_event_log_pub" to the schema.
    /// </summary>
    private const int MaxLength = 48;

    public static string For(object fixture)
    {
        var name = $"dbx_{fixture.GetType().Name.ToLowerInvariant()}";

        if (name.Length > MaxLength)
        {
            throw new InvalidOperationException(
                $"Store name '{name}' exceeds {MaxLength} characters. Shorten the fixture's name or give its " +
                "harness an explicit name.");
        }

        return name;
    }
}