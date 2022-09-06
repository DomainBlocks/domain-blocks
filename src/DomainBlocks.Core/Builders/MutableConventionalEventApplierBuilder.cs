using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DomainBlocks.Core.Builders;

public class MutableConventionalEventApplierBuilder<TAggregate, TEventBase> : IIncludeNonPublicMethodsBuilder
{
    private string _methodName;
    private bool _includeNonPublicMethods;

    public IIncludeNonPublicMethodsBuilder FromMethodName(string methodName)
    {
        _methodName = methodName;
        return this;
    }

    void IIncludeNonPublicMethodsBuilder.IncludeNonPublicMethods()
    {
        _includeNonPublicMethods = true;
    }

    public Action<TAggregate, TEventBase> Build()
    {
        var bindingFlags = BindingFlags.Public | BindingFlags.Instance;

        if (_includeNonPublicMethods)
        {
            bindingFlags |= BindingFlags.NonPublic;
        }

        var methods = from method in typeof(TAggregate).GetMethods(bindingFlags)
            where method.Name == _methodName
            let @params = method.GetParameters()
            where @params.Length == 1
            let paramType = @params[0].ParameterType
            where paramType.IsClass
            where !paramType.IsAbstract
            where typeof(TEventBase).IsAssignableFrom(paramType)
            select (method, paramType);

        var aggregateParam = Expression.Parameter(typeof(TAggregate), "aggregate");

        var handlers = methods
            .Select(x =>
            {
                var (method, eventType) = x;
                var eventParam = Expression.Parameter(typeof(TEventBase), "event");
                var body = Expression.Call(aggregateParam, method, Expression.Convert(eventParam, eventType));
                var block = Expression.Block(aggregateParam, body);
                var lambda = Expression.Lambda<Action<TAggregate, TEventBase>>(block, aggregateParam, eventParam);
                var handler = lambda.Compile();
                return (eventType, handler);
            })
            .ToDictionary(x => x.eventType, x => x.handler);

        return (agg, e) =>
        {
            if (handlers.TryGetValue(e.GetType(), out var handler))
            {
                handler(agg, e);
            }
        };
    }
}