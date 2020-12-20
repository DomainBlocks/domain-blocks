using System.Collections.Generic;

namespace DomainLib.Projections.Sql
{
    public interface ISqlParameterBindingMap<in TEvent>
    {
        IEnumerable<(string name, object value)> GetParameterNamesAndValues(TEvent @event);
        IEnumerable<string> GetParameterNames();
    }
}