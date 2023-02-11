using System.Linq.Expressions;
using System.Reflection;

namespace DomainBlocks.Core.Builders;

public interface IAutoEventTypeBuilder : IMethodVisibilityBuilder
{
    /// <summary>
    /// Specify the name of the event applier method overloads on the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IMethodVisibilityBuilder WithMethodName(string methodName);
}

public interface IMethodVisibilityBuilder
{
    /// <summary>
    /// Specify to include non-public event applier methods.
    /// </summary>
    void IncludeNonPublicMethods();
}

internal sealed class AutoAggregateEventTypeBuilder<TAggregate, TEventBase> :
    IAutoEventTypeBuilder,
    IAutoAggregateEventTypeBuilder<TAggregate>
{
    private enum Mode
    {
        Mutable,
        Immutable,
        ImmutableNonMember
    }

    private readonly Mode _mode;
    private readonly Type _sourceType;
    private string _methodName = "Apply";
    private bool _includeNonPublicMethods;

    private AutoAggregateEventTypeBuilder(Mode mode, Type? sourceType = null)
    {
        _mode = mode;
        _sourceType = sourceType ?? typeof(TAggregate);
    }

    public static AutoAggregateEventTypeBuilder<TAggregate, TEventBase> Mutable()
    {
        return new AutoAggregateEventTypeBuilder<TAggregate, TEventBase>(Mode.Mutable);
    }

    public static AutoAggregateEventTypeBuilder<TAggregate, TEventBase> Immutable()
    {
        return new AutoAggregateEventTypeBuilder<TAggregate, TEventBase>(Mode.Immutable);
    }

    public static AutoAggregateEventTypeBuilder<TAggregate, TEventBase> ImmutableNonMember(Type sourceType)
    {
        return new AutoAggregateEventTypeBuilder<TAggregate, TEventBase>(Mode.ImmutableNonMember, sourceType);
    }

    public IMethodVisibilityBuilder WithMethodName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
            throw new ArgumentException("Method name cannot be null or whitespace.", nameof(methodName));

        _methodName = methodName;
        return this;
    }

    public void IncludeNonPublicMethods()
    {
        _includeNonPublicMethods = true;
    }

    public IEnumerable<AggregateEventType<TAggregate>> Build()
    {
        var bindingFlags = _includeNonPublicMethods ? BindingFlags.NonPublic : BindingFlags.Default;

        var eventTypes = (_mode switch
            {
                Mode.Mutable => BuildEventTypes(bindingFlags, false),
                Mode.Immutable => BuildEventTypes(bindingFlags, true),
                Mode.ImmutableNonMember => BuildNonMemberEventTypes(bindingFlags),
                _ => throw new ArgumentOutOfRangeException() // Should never happen
            })
            .ToList();

        if (eventTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Unable to auto configure events. No appropriate event applier methods named '{_methodName}' could " +
                $"be found in type {typeof(TAggregate).Name}.");
        }

        return eventTypes;
    }

    private IEnumerable<AggregateEventType<TAggregate>> BuildEventTypes(BindingFlags bindingFlags, bool isImmutable)
    {
        bindingFlags |= BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        var methods = from method in _sourceType.GetMethods(bindingFlags)
            where method.Name == _methodName
            let @params = method.GetParameters()
            where @params.Length == 1
            let eventParamType = @params[0].ParameterType
            let returnParamType = method.ReturnParameter!.ParameterType
            where IsEventClrType(eventParamType)
            where isImmutable ? IsAggregateClrType(returnParamType) : IsVoid(returnParamType)
            select method;

        var aggregateParam = Expression.Parameter(typeof(TAggregate), "aggregate");
        var eventParam = Expression.Parameter(typeof(object), "event");

        foreach (var method in methods)
        {
            var @params = method.GetParameters();
            var eventClrType = @params[0].ParameterType;
            var eventType = new AggregateEventType<TAggregate>(eventClrType);

            var instance = method.IsStatic ? null : aggregateParam;
            var call = Expression.Call(instance, method, Expression.Convert(eventParam, eventClrType));

            // Immutable
            if (IsAggregateClrType(method.ReturnType))
            {
                var lambda = Expression.Lambda<Func<TAggregate, object, TAggregate>>(call, aggregateParam, eventParam);
                yield return eventType.SetEventApplier(lambda.Compile());
            }

            // Mutable
            if (IsVoid(method.ReturnType))
            {
                var lambda = Expression.Lambda<Action<TAggregate, object>>(call, aggregateParam, eventParam);
                yield return eventType.SetEventApplier(lambda.Compile());
            }
        }
    }

    private IEnumerable<AggregateEventType<TAggregate>> BuildNonMemberEventTypes(BindingFlags bindingFlags)
    {
        bindingFlags |= BindingFlags.Public | BindingFlags.Static;

        var methods = from method in _sourceType.GetMethods(bindingFlags)
            where method.Name == _methodName
            let @params = method.GetParameters()
            where @params.Length == 2
            let aggregateParamType = @params[0].ParameterType
            let eventParamType = @params[1].ParameterType
            let returnParamType = method.ReturnParameter!.ParameterType
            where IsAggregateClrType(aggregateParamType)
            where IsEventClrType(eventParamType)
            where IsAggregateClrType(returnParamType)
            select method;

        var aggregateParam = Expression.Parameter(typeof(TAggregate), "aggregate");
        var eventParam = Expression.Parameter(typeof(object), "event");

        foreach (var method in methods)
        {
            var @params = method.GetParameters();
            var eventClrType = @params[1].ParameterType;
            var call = Expression.Call(null, method, aggregateParam, Expression.Convert(eventParam, eventClrType));
            var lambda = Expression.Lambda<Func<TAggregate, object, TAggregate>>(call, aggregateParam, eventParam);
            yield return new AggregateEventType<TAggregate>(eventClrType).SetEventApplier(lambda.Compile());
        }
    }

    private static bool IsEventClrType(Type type) =>
        type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(TEventBase));

    private static bool IsAggregateClrType(Type type) => type.IsAssignableTo(typeof(TAggregate));

    private static bool IsVoid(Type type) => type == typeof(void);
}