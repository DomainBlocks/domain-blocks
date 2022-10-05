using System;
using System.Collections.Generic;

namespace DomainBlocks.Projections.Sql;

public delegate IDictionary<string, Func<TEvent, object>> GetParameterBindings<TEvent>();