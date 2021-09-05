using System.Collections.Generic;
using System.Data;

namespace DomainBlocks.Projections.Sql.Tests.Fakes
{
    public class FakeDbConnection : IDbConnection
    {
        public string ConnectionString { get; set; }
        public int ConnectionTimeout { get; }
        public string Database { get; }
        public ConnectionState State { get; private set; }
        public FakeDbTransaction ActiveTransaction { get; set; }
        public List<FakeDbCommand> ExecutedCommands { get; } = new();
        public List<FakeDbTransaction> TransactionsBegun { get; } = new();

        public void Open()
        {
            State = ConnectionState.Open;
        }

        public void Close()
        {
            State = ConnectionState.Closed;
        }

        public IDbCommand CreateCommand()
        {
            var command = new FakeDbCommand(this);
            return command;
        }

        public IDbTransaction BeginTransaction()
        {
            return BeginTransaction(IsolationLevel.Unspecified);
        }

        public IDbTransaction BeginTransaction(IsolationLevel il)
        {
            ActiveTransaction = new FakeDbTransaction(this, il);
            TransactionsBegun.Add(ActiveTransaction);
            return ActiveTransaction;
        }

        public void ChangeDatabase(string databaseName)
        {
            throw new System.NotImplementedException();
        }

        public void Dispose()
        {
        }
    }
}