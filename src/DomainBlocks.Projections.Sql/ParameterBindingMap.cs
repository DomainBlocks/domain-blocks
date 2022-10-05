using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections.Sql;

public sealed class ParameterBindingMap<TEvent> : ISqlParameterBindingMap<TEvent>
{
    private readonly IDictionary<string, Func<TEvent, object>> _bindingMap;

    public ParameterBindingMap(IDictionary<string, Func<TEvent, object>> bindingMap)
    {
        _bindingMap = bindingMap;
    }

    public ParameterBindingMap(GetParameterBindings<TEvent> getBindings)
    {
        _bindingMap = getBindings();
    }

    public IEnumerable<(string name, object value)> GetParameterNamesAndValues(TEvent @event)
    {
        return _bindingMap.Select(kvp => (kvp.Key, kvp.Value(@event)));
    }

    public IEnumerable<string> GetParameterNames()
    {
        return _bindingMap.Keys;
    }
}