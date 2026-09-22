using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Tests.Shared;

/// <summary>
/// Makes the filters over a stored payload that a predicate is lowered to, for tests of what makes them and of what
/// translates them. Nothing else can make one: a caller selects by the payload with a predicate.
/// </summary>
internal readonly struct StoredPayload(string path)
{
    public static StoredPayload At(string path) => new(path);

    public EventFilter EqualTo(string value) => Compare(PayloadComparison.Equal, PayloadValue.From(value));

    public EventFilter EqualTo(decimal value) => Compare(PayloadComparison.Equal, PayloadValue.From(value));

    public EventFilter EqualTo(bool value) => Compare(PayloadComparison.Equal, PayloadValue.From(value));

    public EventFilter NotEqualTo(string value) => Compare(PayloadComparison.NotEqual, PayloadValue.From(value));

    public EventFilter NotEqualTo(decimal value) => Compare(PayloadComparison.NotEqual, PayloadValue.From(value));

    public EventFilter NotEqualTo(bool value) => Compare(PayloadComparison.NotEqual, PayloadValue.From(value));

    public EventFilter GreaterThan(decimal value) => Compare(PayloadComparison.GreaterThan, PayloadValue.From(value));

    public EventFilter GreaterThanOrEqualTo(decimal value) =>
        Compare(PayloadComparison.GreaterThanOrEqual, PayloadValue.From(value));

    public EventFilter LessThan(decimal value) => Compare(PayloadComparison.LessThan, PayloadValue.From(value));

    public EventFilter LessThanOrEqualTo(decimal value) =>
        Compare(PayloadComparison.LessThanOrEqual, PayloadValue.From(value));

    private PayloadValueFilter Compare(PayloadComparison comparison, PayloadValue value) => new(path, comparison, value);
}