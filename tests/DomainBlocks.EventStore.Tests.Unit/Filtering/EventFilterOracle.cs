using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

/// <summary>
/// Says whether a filter matches an event without asking the filter: what each kind of filter means is written out
/// here a second time, so that what a filter does can be held against it. It also evaluates the filters that are for a
/// database alone, over the payload as it is stored, as a database that can see into its payloads would.
/// </summary>
internal static class EventFilterOracle
{
    private static readonly ConcurrentDictionary<LambdaExpression, Delegate> Predicates = new();

    public static bool Matches(EventFilter filter, StoredEvent e)
    {
        return filter switch
        {
            AllEventsFilter => true,
            NoEventsFilter => false,
            AndFilter conjunction => conjunction.Operands.All(x => Matches(x, e)),
            OrFilter disjunction => disjunction.Operands.Any(x => Matches(x, e)),
            NotFilter negation => !Matches(negation.Operand, e),
            EventNameFilter byName => byName.Names.Contains(e.EventName, StringComparer.Ordinal),
            StreamIdFilter byStream => byStream.Ids.Contains(e.StreamId, StringComparer.Ordinal),
            StreamIdPrefixFilter byPrefix => e.StreamId.StartsWith(byPrefix.Prefix, StringComparison.Ordinal),
            MetadataExistsFilter byKey => e.Metadata.ContainsKey(byKey.Key),

            MetadataValueFilter byValue => e.Metadata.TryGetValue(byValue.Key, out var value) &&
                                           byValue.Values.Contains(value, StringComparer.Ordinal),

            CreatedAtFilter byTime => (byTime.From is null || e.CreatedAt >= byTime.From) &&
                                      (byTime.Before is null || e.CreatedAt < byTime.Before),

            EventTypeFilter byType => byType.EventType.IsInstanceOfType(e.DecodedPayload) &&
                                      IsTrueOf(byType.Predicate, e.DecodedPayload),

            PayloadValueFilter byPayload =>
                ValuesAt(e.StoredPayload, byPayload.PathSegments).Any(x => Compares(x, byPayload)),

            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };
    }

    // An event that the predicate cannot be asked of, for a null on the way to a member, is not one it is true of.
    private static bool IsTrueOf(LambdaExpression? predicate, object e)
    {
        if (predicate is null)
            return true;

        try
        {
            return (bool)Predicates.GetOrAdd(predicate, x => x.Compile()).DynamicInvoke(e)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException)
        {
            return false;
        }
    }

    // Names match exactly, and an array stands for each of its elements, one level deep, on the way along a path and
    // at the end of it. A payload that is stored as bytes has nothing at any path.
    private static IEnumerable<JsonElement> ValuesAt(string? json, IReadOnlyList<string> path)
    {
        if (json is null)
            return [];

        // Cloned, as the elements are read after the document is gone.
        using var document = JsonDocument.Parse(json);

        IEnumerable<JsonElement> current = [document.RootElement.Clone()];

        foreach (var name in path)
        {
            current =
            [
                .. from element in current.SelectMany(ElementsOf)
                where element.ValueKind == JsonValueKind.Object
                from property in element.EnumerateObject()
                where property.NameEquals(name)
                select property.Value
            ];
        }

        return [.. current.SelectMany(ElementsOf)];
    }

    private static IEnumerable<JsonElement> ElementsOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [element];

    // A value compares only with one of its own kind.
    private static bool Compares(JsonElement stored, PayloadValueFilter filter)
    {
        var value = filter.Value;

        switch (stored.ValueKind, value.Kind)
        {
            case (JsonValueKind.String, PayloadValueKind.Text):
                var areSameText = string.Equals(stored.GetString(), value.Text, StringComparison.Ordinal);
                return filter.Comparison switch
                {
                    PayloadComparison.Equal => areSameText,
                    PayloadComparison.NotEqual => !areSameText,
                    _ => false
                };

            case (JsonValueKind.True or JsonValueKind.False, PayloadValueKind.Boolean):
                var areSameBoolean = stored.GetBoolean() == value.Boolean;
                return filter.Comparison switch
                {
                    PayloadComparison.Equal => areSameBoolean,
                    PayloadComparison.NotEqual => !areSameBoolean,
                    _ => false
                };

            case (JsonValueKind.Number, PayloadValueKind.Number):
                var order = stored.GetDecimal().CompareTo(value.Number);
                return filter.Comparison switch
                {
                    PayloadComparison.Equal => order == 0,
                    PayloadComparison.NotEqual => order != 0,
                    PayloadComparison.GreaterThan => order > 0,
                    PayloadComparison.GreaterThanOrEqual => order >= 0,
                    PayloadComparison.LessThan => order < 0,
                    _ => order <= 0
                };

            default:
                return false;
        }
    }
}