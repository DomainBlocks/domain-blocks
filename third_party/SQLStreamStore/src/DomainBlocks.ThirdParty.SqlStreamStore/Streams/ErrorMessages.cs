﻿namespace DomainBlocks.ThirdParty.SqlStreamStore.Streams
{
    public static class ErrorMessages
    {
        public static string AppendFailedWrongExpectedVersion(string streamId, int expectedVersion) 
            => $"Append failed due to WrongExpectedVersion.Stream: {streamId}, Expected version: {expectedVersion}";

        public static string DeleteStreamFailedWrongExpectedVersion(string streamId, int expectedVersion)
            => $"Delete stream failed due to WrongExpectedVersion.Stream: {streamId}, Expected version: {expectedVersion}.";
    }
}