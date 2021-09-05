using System;
using System.Data;

#pragma warning disable 8632

namespace DomainBlocks.Projections.Sql.Tests.Fakes
{
    public class FakeDbCommand : IDbCommand
    {
        public FakeDbCommand(IDbConnection? connection)
        {
            Connection = connection;
        }

        public void Dispose()
        {
        }

        public void Cancel()
        {
            throw new NotImplementedException();
        }

        public IDbDataParameter CreateParameter()
        {
            throw new NotImplementedException();
        }

        public int ExecuteNonQuery()
        {
            ((FakeDbConnection) Connection)?.ExecutedCommands.Add(this);
            Executed = true;
            return 1;
        }

        public IDataReader ExecuteReader()
        {
            throw new NotImplementedException();
        }

        public IDataReader ExecuteReader(CommandBehavior behavior)
        {
            throw new NotImplementedException();
        }

        public object? ExecuteScalar()
        {
            throw new NotImplementedException();
        }

        public void Prepare()
        {
            throw new NotImplementedException();
        }

        public string CommandText { get; set; }
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; }
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters { get; }
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }
        public bool Executed { get; private set; }
    }
}