using System.Security.Cryptography;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Generates the names of a store's replication slots from its schema name and a random suffix per slot. The schema
/// name tells which store's slots they are, and the suffix keeps a new slot from taking the name of one that the server
/// hasn't dropped yet.
/// </summary>
internal sealed class SlotNameGenerator(SchemaObjectNames names)
{
    private const int MaxSlotNameLength = 63;
    private const int SuffixLength = 12;

    /// <summary>
    /// Gets a PostgreSQL regular expression that matches the names of the schema's slots. The suffix has a fixed form,
    /// so the expression doesn't match the slots of a schema whose name starts with this one. The schema name needs no
    /// escaping, since it contains only lowercase letters, digits, and underscores.
    /// </summary>
    public string NamePattern => $"^{names.Schema}_[0-9a-f]{{{SuffixLength}}}$";

    public string Next()
    {
        var name = $"{names.Schema}_{RandomNumberGenerator.GetHexString(SuffixLength, lowercase: true)}";

        if (name.Length > MaxSlotNameLength)
        {
            throw new InvalidOperationException(
                $"Slot name '{name}' exceeds {MaxSlotNameLength} characters. Live subscriptions need a schema name " +
                $"of at most {MaxSlotNameLength - SuffixLength - 1} characters.");
        }

        return name;
    }
}