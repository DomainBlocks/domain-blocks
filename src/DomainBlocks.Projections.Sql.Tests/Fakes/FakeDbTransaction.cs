using System;
using System.Data;

#pragma warning disable 8632

namespace DomainBlocks.Projections.Sql.Tests.Fakes
{
    public class FakeDbTransaction : IDbTransaction
    {
        public FakeDbTransaction(IDbConnection? connection, IsolationLevel isolationLevel)
        {
            Connection = connection;
            Id = Guid.NewGuid();
            IsolationLevel = isolationLevel;
        }

        public void Dispose()
        {
        }

        public void Commit()
        {
            HasBeenCommitted = true;
        }

        public void Rollback()
        {
            HasBeenRolledBack = true;
        }

        public IDbConnection? Connection { get; }
        public Guid Id { get; }
        public IsolationLevel IsolationLevel { get; }
        public bool HasBeenRolledBack { get; private set; }
        public bool HasBeenCommitted { get; private set; }
    }
}