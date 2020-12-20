using System;
using System.Collections.Generic;

namespace DomainLib.Projections.Sql
{
    public delegate IDictionary<string, Func<TEvent, object>> GetParameterBindings<TEvent>();
}