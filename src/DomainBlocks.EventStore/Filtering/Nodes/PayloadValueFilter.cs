using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events whose stored payload has, at <see cref="Path"/>, a value of the kind of <see cref="Value"/> that
/// compares with it as <see cref="Comparison"/> says. An array stands for its elements, so it matches if any of them
/// does, and <see cref="PayloadComparison.NotEqual"/> asks for a value of that kind that differs, so it is not the
/// negation of <see cref="PayloadComparison.Equal"/>.
/// </summary>
/// <remarks>
/// It is what a predicate over an event is lowered to, for a database that can see into its payloads, and is only ever
/// part of what a store gives its database. See <see cref="EventFilterPlan"/>. A caller selects by the payload with a
/// predicate: <see cref="EventFilter.OfType{TEvent}(System.Linq.Expressions.Expression{Func{TEvent, bool}})"/>.
/// </remarks>
public sealed record PayloadValueFilter : EventFilter
{
    internal PayloadValueFilter(string path, PayloadComparison comparison, PayloadValue value)
    {
        Path = path;
        PathSegments = GetPathSegments(path);
        Comparison = comparison;
        Value = value;
    }

    /// <summary>
    /// Property names, as they are stored, separated by dots.
    /// </summary>
    public string Path { get; }

    public IReadOnlyList<string> PathSegments { get; }

    public PayloadComparison Comparison { get; }

    public PayloadValue Value { get; }

    internal override int Cost => 2;

    /// <summary>
    /// Not supported: the events that a database has not ruled out are tested against the predicate itself.
    /// </summary>
    public override bool Matches(IFilterableEvent filterable) =>
        throw new UnreachableException($"The filter '{this}' is for a database to evaluate.");

    protected override void WriteTo(StringBuilder text) =>
        Write(text, "payload", [Path, Comparison.ToString(), Value.ToString()]);

    public bool Equals(PayloadValueFilter? other) =>
        other is not null && Path == other.Path && Comparison == other.Comparison && Value == other.Value;

    public override int GetHashCode() => HashCode.Combine(Path, Comparison, Value);

    private static ImmutableArray<string> GetPathSegments(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var segments = path.Split('.');

        return segments.All(x => x.Length > 0)
            ? [.. segments]
            : throw new ArgumentException($"The payload path '{path}' has an empty property name.", nameof(path));
    }
}