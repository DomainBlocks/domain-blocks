using System.Data;

namespace DomainLib.Projections.Sql
{
    public interface IDbConnector
    {
        SqlContextSettings ContextSettings => SqlContextSettings.Default;
        IDbConnection Connection { get; }
        void BindParameters<TEvent>(IDbCommand command,
                                    TEvent @event,
                                    SqlColumnDefinitions columnDefinitions,
                                    ISqlParameterBindingMap<TEvent> parameterBindingMap);
    }
}