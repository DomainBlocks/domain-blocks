﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DomainBlocks.Core.Builders;

public sealed class ImmutableAutoEventOptionsBuilder<TAggregate, TEventBase>
{
    private string _methodName = "Apply";
    private bool _includeNonPublicMethods;

    /// <summary>
    /// Specify the name of event applier methods on the aggregate type, e.g. if "ApplyEvent" is specified, then method
    /// overloads with the signature <code>TAggregate ApplyEvent(SomeEvent)</code> will be used, where SomeEvent is
    /// derived from <see cref="TEventBase"/>.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public ImmutableAutoEventOptionsBuilder<TAggregate, TEventBase> WithName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
            throw new ArgumentException("Method name cannot be null or whitespace.", nameof(methodName));

        _methodName = methodName;
        return this;
    }

    /// <summary>
    /// Specify to include non-public event applier methods.
    /// </summary>
    public ImmutableAutoEventOptionsBuilder<TAggregate, TEventBase> IncludeNonPublic()
    {
        _includeNonPublicMethods = true;
        return this;
    }

    internal IEnumerable<EventOptions<TAggregate>> Build()
    {
        var bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        if (_includeNonPublicMethods)
        {
            bindingFlags |= BindingFlags.NonPublic;
        }

        var methods = from method in typeof(TAggregate).GetMethods(bindingFlags)
            where method.Name == _methodName
            let @params = method.GetParameters()
            where @params.Length == 1
            let paramType = @params[0].ParameterType
            let returnParam = method.ReturnParameter
            where typeof(TAggregate).IsAssignableFrom(returnParam.ParameterType)
            where paramType.IsClass
            where !paramType.IsAbstract
            where typeof(TEventBase).IsAssignableFrom(paramType)
            select (method, paramType);

        var aggregateParam = Expression.Parameter(typeof(TAggregate), "aggregate");

        var eventOptions = methods
            .Select(x =>
            {
                var (method, eventType) = x;
                var eventParam = Expression.Parameter(typeof(object), "event");
                var instance = method.IsStatic ? null : aggregateParam;
                var body = Expression.Call(instance, method, Expression.Convert(eventParam, eventType));
                var block = Expression.Block(aggregateParam, body);
                var lambda = Expression.Lambda<Func<TAggregate, object, TAggregate>>(block, aggregateParam, eventParam);
                var applier = lambda.Compile();
                return new EventOptions<TAggregate>(eventType).WithEventApplier(applier);
            })
            .ToList();

        if (eventOptions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Unable to auto configure events. No appropriate event applier methods named '{_methodName}' could " +
                $"be found on type {typeof(TAggregate).Name}.");
        }

        return eventOptions;
    }
}