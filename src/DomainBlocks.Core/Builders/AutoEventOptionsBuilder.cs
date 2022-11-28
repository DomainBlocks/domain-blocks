using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DomainBlocks.Core.Builders;

public interface ICanIncludeNonPublicMethodsOrUseEventTypesOnly : ICanUseEventTypesOnly
{
    /// <summary>
    /// Specify to include non-public event applier methods.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    ICanUseEventTypesOnly IncludeNonPublicMethods();
}

public interface ICanUseEventTypesOnly
{
    /// <summary>
    /// Specify to use the event types only, and ignore the event apply methods. Use this option for scenarios where a
    /// manually written event applier (e.g. with a switch expression) is preferred.
    /// </summary>
    void UseEventTypesOnly();
}

public sealed class AutoEventOptionsBuilder<TAggregate, TEventBase> :
    ICanIncludeNonPublicMethodsOrUseEventTypesOnly,
    IAutoEventOptionsBuilder<TAggregate>
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
    private bool _useEventsTypesOnly;

    private AutoEventOptionsBuilder(Mode mode, Type sourceType = null)
    {
        _mode = mode;
        _sourceType = sourceType ?? typeof(TAggregate);
    }

    internal static AutoEventOptionsBuilder<TAggregate, TEventBase> Mutable()
    {
        return new AutoEventOptionsBuilder<TAggregate, TEventBase>(Mode.Mutable);
    }

    internal static AutoEventOptionsBuilder<TAggregate, TEventBase> Immutable()
    {
        return new AutoEventOptionsBuilder<TAggregate, TEventBase>(Mode.Immutable);
    }

    internal static AutoEventOptionsBuilder<TAggregate, TEventBase> ImmutableNonMember(Type sourceType)
    {
        return new AutoEventOptionsBuilder<TAggregate, TEventBase>(Mode.ImmutableNonMember, sourceType);
    }

    /// <summary>
    /// Specify the name of the event applier method overloads on the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public ICanIncludeNonPublicMethodsOrUseEventTypesOnly WithMethodName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
            throw new ArgumentException("Method name cannot be null or whitespace.", nameof(methodName));

        _methodName = methodName;
        return this;
    }

    public ICanUseEventTypesOnly IncludeNonPublicMethods()
    {
        _includeNonPublicMethods = true;
        return this;
    }

    public void UseEventTypesOnly()
    {
        _useEventsTypesOnly = true;
    }

    IEnumerable<EventOptions<TAggregate>> IAutoEventOptionsBuilder<TAggregate>.Build()
    {
        var bindingFlags = _includeNonPublicMethods ? BindingFlags.NonPublic : BindingFlags.Default;

        var eventOptions = (_mode switch
            {
                Mode.Mutable => BuildEventOptions(bindingFlags, false),
                Mode.Immutable => BuildEventOptions(bindingFlags, true),
                Mode.ImmutableNonMember => BuildNonMemberEventOptions(bindingFlags),
                _ => throw new ArgumentOutOfRangeException() // Should never happen
            })
            .ToList();

        if (eventOptions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Unable to auto configure events. No appropriate event applier methods named '{_methodName}' could " +
                $"be found in type {typeof(TAggregate).Name}.");
        }

        return eventOptions;
    }

    private IEnumerable<EventOptions<TAggregate>> BuildEventOptions(BindingFlags bindingFlags, bool isImmutable)
    {
        bindingFlags |= BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        var methods = from method in _sourceType.GetMethods(bindingFlags)
            where method.Name == _methodName
            let @params = method.GetParameters()
            where @params.Length == 1
            let eventParamType = @params[0].ParameterType
            let returnParamType = method.ReturnParameter!.ParameterType
            where IsEventType(eventParamType)
            where isImmutable ? IsAggregateType(returnParamType) : IsVoid(returnParamType)
            select method;

        var aggregateParam = Expression.Parameter(typeof(TAggregate), "aggregate");
        var eventParam = Expression.Parameter(typeof(object), "event");

        foreach (var method in methods)
        {
            var @params = method.GetParameters();
            var eventType = @params[0].ParameterType;
            var options = new EventOptions<TAggregate>(eventType);

            if (_useEventsTypesOnly)
            {
                yield return options;
                continue;
            }

            var instance = method.IsStatic ? null : aggregateParam;
            var call = Expression.Call(instance, method, Expression.Convert(eventParam, eventType));

            // Immutable
            if (IsAggregateType(method.ReturnType))
            {
                var lambda = Expression.Lambda<Func<TAggregate, object, TAggregate>>(call, aggregateParam, eventParam);
                yield return options.WithEventApplier(lambda.Compile());
            }

            // Mutable
            if (IsVoid(method.ReturnType))
            {
                var lambda = Expression.Lambda<Action<TAggregate, object>>(call, aggregateParam, eventParam);
                yield return options.WithEventApplier(lambda.Compile());
            }
        }
    }

    private IEnumerable<EventOptions<TAggregate>> BuildNonMemberEventOptions(BindingFlags bindingFlags)
    {
        bindingFlags |= BindingFlags.Public | BindingFlags.Static;

        var methods = from method in _sourceType.GetMethods(bindingFlags)
            where method.Name == _methodName
            let @params = method.GetParameters()
            where @params.Length == 2
            let aggregateParamType = @params[0].ParameterType
            let eventParamType = @params[1].ParameterType
            let returnParamType = method.ReturnParameter!.ParameterType
            where IsAggregateType(aggregateParamType)
            where IsEventType(eventParamType)
            where IsAggregateType(returnParamType)
            select method;

        var aggregateParam = Expression.Parameter(typeof(TAggregate), "aggregate");
        var eventParam = Expression.Parameter(typeof(object), "event");

        foreach (var method in methods)
        {
            var @params = method.GetParameters();
            var eventType = @params[1].ParameterType;
            var options = new EventOptions<TAggregate>(eventType);

            if (_useEventsTypesOnly)
            {
                yield return options;
                continue;
            }

            var call = Expression.Call(null, method, aggregateParam, Expression.Convert(eventParam, eventType));
            var lambda = Expression.Lambda<Func<TAggregate, object, TAggregate>>(call, aggregateParam, eventParam);
            yield return options.WithEventApplier(lambda.Compile());
        }
    }

    private static bool IsEventType(Type type) =>
        type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(TEventBase));

    private static bool IsAggregateType(Type type) => type.IsAssignableTo(typeof(TAggregate));

    private static bool IsVoid(Type type) => type == typeof(void);
}