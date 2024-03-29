﻿using Npgsql;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Postgres
{
    internal static class NpgsqlExceptionExtensions
    {
        public static bool IsWrongExpectedVersion(this PostgresException exception)
            => exception.MessageText.Equals("WrongExpectedVersion");

        public static bool IsDeadlock(this PostgresException ex) => ex.SqlState == "40P01";
    }
}