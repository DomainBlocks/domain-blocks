using System.Collections.Generic;

namespace DomainBlocks.Projections.Sql;

public interface ISqlParameterBindingMap<in TEvent>
{
    IEnumerable<(string name, object value)> GetParameterNamesAndValues(TEvent @event);
    IEnumerable<string> GetParameterNames();
}