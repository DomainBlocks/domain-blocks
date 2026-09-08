using System.Text.RegularExpressions;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Generates replication slot names that are unique per store instance and per session, so that a reconnecting feed
/// never collides with a slot the server has not yet cleaned up.
/// </summary>
internal sealed partial class SlotNameGenerator
{
    private const int MaxSlotNameLength = 63;
    private readonly string _prefix;
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..12];
    private int _counter;

    public SlotNameGenerator(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        if (!SlotNamePrefixRegex().IsMatch(prefix))
        {
            throw new ArgumentException(
                $"Slot name prefix '{prefix}' is invalid. It must match {SlotNamePrefixRegex()}.",
                nameof(prefix));
        }

        _prefix = prefix;
    }

    public string Next()
    {
        var name = $"{_prefix}_{_instanceId}_{Interlocked.Increment(ref _counter)}";

        if (name.Length > MaxSlotNameLength)
            throw new InvalidOperationException($"Slot name '{name}' exceeds {MaxSlotNameLength} characters.");

        return name;
    }

    [GeneratedRegex("^[a-z0-9_]{1,32}$")]
    private static partial Regex SlotNamePrefixRegex();
}
