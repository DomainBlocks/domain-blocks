﻿using System;
using System.Threading.Tasks;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using Shouldly;
using Xunit;

namespace DomainBlocks.ThirdParty.SqlStreamStore
{
    public class StreamEventTests
    {
        [Fact]
        public async Task Can_deserialize()
        {
            var message = new StreamMessage(
                "stream",
                Guid.NewGuid(),
                1,
                2,
                DateTime.UtcNow,
                "type",
                "\"meta\"", "\"data\"");

            (await message.GetJsonDataAs<string>()).ShouldBe("data");
            message.JsonMetadataAs<string>().ShouldBe("meta");
        }
    }
}