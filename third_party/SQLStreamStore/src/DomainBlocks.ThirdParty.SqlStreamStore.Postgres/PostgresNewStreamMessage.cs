﻿using DomainBlocks.ThirdParty.SqlStreamStore.Streams;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Postgres
{
    internal class PostgresNewStreamMessage
    {
        public Guid MessageId { get; set; }
        public string JsonData { get; set; }
        public string JsonMetadata { get; set; }
        public string Type { get; set; }

        public static PostgresNewStreamMessage FromNewStreamMessage(NewStreamMessage message)
            => new PostgresNewStreamMessage
            {
                MessageId = message.MessageId,
                Type = message.Type,
                JsonData = message.JsonData,
                JsonMetadata = string.IsNullOrEmpty(message.JsonMetadata) ? null : message.JsonMetadata,
            };
    }
}