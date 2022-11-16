using System.Data;

namespace DomainBlocks.Projections.Sql.Tests.Fakes
{
    public class FakeDbConnector : IDbConnector
    {
        public FakeDbConnector(SqlContextSettings contextSettings = null)
        {
            ContextSettings = contextSettings ?? SqlContextSettings.Default;
            Connection = new FakeDbConnection();
        }

        public void BindParameters<TEvent>(IDbCommand command,
                                           TEvent @event,
                                           SqlColumnDefinitions columnDefinitions,
                                           ISqlParameterBindingMap<TEvent> parameterBindingMap)
        {
        }

        public SqlContextSettings ContextSettings { get; }
        IDbConnection IDbConnector.Connection => Connection;
        public FakeDbConnection Connection { get; }

    }
}
