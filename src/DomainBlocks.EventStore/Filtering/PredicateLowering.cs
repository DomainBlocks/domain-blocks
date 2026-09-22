using System.Collections.Frozen;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Lowers a predicate over an event to a filter over its stored payload, allowing a database to narrow the read before
/// the predicate is tested. The filter rules out an event only when a stored value contradicts the predicate.
/// </summary>
internal static class PredicateLowering
{
    // The range of each integer type. Conversions are followed only when the target type can represent every value of
    // the source type.
    private static readonly FrozenDictionary<Type, (decimal Min, decimal Max)> IntegerRanges =
        new KeyValuePair<Type, (decimal Min, decimal Max)>[]
            {
                new(typeof(sbyte), (sbyte.MinValue, sbyte.MaxValue)),
                new(typeof(byte), (byte.MinValue, byte.MaxValue)),
                new(typeof(short), (short.MinValue, short.MaxValue)),
                new(typeof(ushort), (ushort.MinValue, ushort.MaxValue)),
                new(typeof(int), (int.MinValue, int.MaxValue)),
                new(typeof(uint), (uint.MinValue, uint.MaxValue)),
                new(typeof(long), (long.MinValue, long.MaxValue)),
                new(typeof(ulong), (ulong.MinValue, ulong.MaxValue))
            }
            .ToFrozenDictionary();

    /// <summary>
    /// Lowers a predicate over an event to a filter over its stored payload for pushdown. The filter only rules out
    /// events when their stored values contradict the predicate.
    /// </summary>
    /// <param name="predicate">A predicate over an event.</param>
    /// <param name="getStoredPath">Resolves the path of event members in the stored payload.</param>
    public static EventFilter Lower(LambdaExpression predicate, Func<IReadOnlyList<MemberInfo>, string?> getStoredPath)
    {
        return LowerImpl(predicate.Body, true);

        // Lower the expression under the requirement that it must be true or false.
        EventFilter LowerImpl(Expression expression, bool mustBeTrue)
        {
            while (true)
            {
                switch (expression)
                {
                    case UnaryExpression { NodeType: ExpressionType.Not } negation when negation.Type == typeof(bool):
                        expression = negation.Operand;
                        mustBeTrue = !mustBeTrue;
                        continue;

                    // A true && requires both sides; a false || requires both sides to be false.
                    case BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.And } both
                        when both.Type == typeof(bool):
                        return mustBeTrue
                            ? LowerImpl(both.Left, true) & LowerImpl(both.Right, true)
                            : LowerImpl(both.Left, false) | LowerImpl(both.Right, false);

                    case BinaryExpression { NodeType: ExpressionType.OrElse or ExpressionType.Or } either
                        when either.Type == typeof(bool):
                        return mustBeTrue
                            ? LowerImpl(either.Left, true) | LowerImpl(either.Right, true)
                            : LowerImpl(either.Left, false) & LowerImpl(either.Right, false);

                    case BinaryExpression comparison when GetComparison(comparison.NodeType) is { } kind:
                    {
                        if (GetStoredPath(comparison.Left) is { } path && GetValue(comparison.Right) is { } value)
                            return Contradict(path, mustBeTrue ? kind : Negate(kind), value);

                        if (GetStoredPath(comparison.Right) is { } rightPath &&
                            GetValue(comparison.Left) is { } leftValue)
                        {
                            return Contradict(
                                rightPath,
                                mustBeTrue ? Converse(kind) : Negate(Converse(kind)),
                                leftValue);
                        }

                        return EventFilter.All;
                    }

                    case MemberExpression member when member.Type == typeof(bool) && GetStoredPath(member) is { } path:
                        return Contradict(path, PayloadComparison.Equal, PayloadValue.From(mustBeTrue));

                    default:
                        return EventFilter.All;
                }
            }
        }

        // Get the stored path when the member's value can be compared safely in its stored representation.
        string? GetStoredPath(Expression expression)
        {
            // A member is often converted for comparison, such as an enum to its number or an int to a long.
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert } conversion &&
                   IsLossless(conversion.Operand.Type, conversion.Type))
            {
                expression = conversion.Operand;
            }

            if (!IsComparable(expression.Type))
                return null;

            var members = new List<MemberInfo>();

            for (; expression is MemberExpression access; expression = access.Expression!)
                members.Insert(0, access.Member);

            return expression == predicate.Parameters[0] && members.Count > 0 ? getStoredPath(members) : null;
        }
    }

    // Build a pushdown that rules out stored values that contradict the comparison.
    private static EventFilter Contradict(string path, PayloadComparison comparison, PayloadValue value)
    {
        var contradiction = Negate(comparison);

        // Only numbers have an ordering in the stored payload.
        if (contradiction is not (PayloadComparison.Equal or PayloadComparison.NotEqual) &&
            value.Kind != PayloadValueKind.Number)
        {
            return EventFilter.All;
        }

        // A boolean contradicts one value by having the other value.
        if (contradiction == PayloadComparison.NotEqual && value.Kind == PayloadValueKind.Boolean)
            return !new PayloadValueFilter(path, PayloadComparison.Equal, PayloadValue.From(!value.Boolean));

        return !new PayloadValueFilter(path, contradiction, value);
    }

    private static PayloadComparison? GetComparison(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Equal => PayloadComparison.Equal,
        ExpressionType.NotEqual => PayloadComparison.NotEqual,
        ExpressionType.GreaterThan => PayloadComparison.GreaterThan,
        ExpressionType.GreaterThanOrEqual => PayloadComparison.GreaterThanOrEqual,
        ExpressionType.LessThan => PayloadComparison.LessThan,
        ExpressionType.LessThanOrEqual => PayloadComparison.LessThanOrEqual,
        _ => null
    };

    // The logical negation of the comparison.
    private static PayloadComparison Negate(PayloadComparison comparison) => comparison switch
    {
        PayloadComparison.Equal => PayloadComparison.NotEqual,
        PayloadComparison.NotEqual => PayloadComparison.Equal,
        PayloadComparison.GreaterThan => PayloadComparison.LessThanOrEqual,
        PayloadComparison.GreaterThanOrEqual => PayloadComparison.LessThan,
        PayloadComparison.LessThan => PayloadComparison.GreaterThanOrEqual,
        PayloadComparison.LessThanOrEqual => PayloadComparison.GreaterThan,
        _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null)
    };

    // The converse of the comparison, with its operands exchanged.
    private static PayloadComparison Converse(PayloadComparison comparison) => comparison switch
    {
        PayloadComparison.Equal => comparison,
        PayloadComparison.NotEqual => comparison,
        PayloadComparison.GreaterThan => PayloadComparison.LessThan,
        PayloadComparison.GreaterThanOrEqual => PayloadComparison.LessThanOrEqual,
        PayloadComparison.LessThan => PayloadComparison.GreaterThan,
        PayloadComparison.LessThanOrEqual => PayloadComparison.GreaterThanOrEqual,
        _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null)
    };

    // Whether the value is stored in a representation that preserves its comparison semantics. Floating-point values
    // are excluded because their stored representation does not preserve comparisons with other numbers.
    private static bool IsComparable(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type == typeof(string) ||
               type == typeof(bool) ||
               type == typeof(decimal) ||
               type.IsEnum ||
               IntegerRanges.ContainsKey(type);
    }

    private static bool IsLossless(Type from, Type to)
    {
        from = Nullable.GetUnderlyingType(from) ?? from;
        to = Nullable.GetUnderlyingType(to) ?? to;

        if (from.IsEnum)
            from = Enum.GetUnderlyingType(from);

        if (from == to)
            return true;

        if (!IntegerRanges.TryGetValue(from, out var fromRange))
            return false;

        return to == typeof(decimal) ||
               IntegerRanges.TryGetValue(to, out var toRange) &&
               toRange.Min <= fromRange.Min &&
               toRange.Max >= fromRange.Max;
    }

    // Get the value of an expression that does not depend on the event. Captured fields and properties are evaluated
    // now. Anything that depends on the event cannot be known for pushdown.
    private static PayloadValue? GetValue(Expression expression)
    {
        return Evaluate(expression) switch
        {
            string text => PayloadValue.From(text),

            bool boolean => PayloadValue.From(boolean),

            // An integer, decimal, or enum value, with enums represented by their underlying number.
            { } number when IsLossless(number.GetType(), typeof(decimal)) =>
                PayloadValue.From(Convert.ToDecimal(number, CultureInfo.InvariantCulture)),

            _ => null
        };

        static object? Evaluate(Expression? expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ConstantExpression constant:
                        return constant.Value;

                    case UnaryExpression { NodeType: ExpressionType.Convert } conversion
                        when IsLossless(conversion.Operand.Type, conversion.Type):
                        expression = conversion.Operand;
                        continue;

                    // A null target has no member value, and a property's getter may define its own behavior.
                    case MemberExpression access:
                        var target = Evaluate(access.Expression);

                        try
                        {
                            return access.Member switch
                            {
                                FieldInfo { IsStatic: true, IsInitOnly: true } field => field.GetValue(null),
                                FieldInfo { IsStatic: false } field when target is not null => field.GetValue(target),

                                PropertyInfo { GetMethod.IsStatic: false } property when target is not null =>
                                    property.GetValue(target),

                                _ => null
                            };
                        }
                        catch (TargetInvocationException)
                        {
                            return null;
                        }

                    default:
                        return null;
                }
            }
        }
    }
}