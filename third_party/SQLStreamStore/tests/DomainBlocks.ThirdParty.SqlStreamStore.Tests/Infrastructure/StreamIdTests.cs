﻿using System;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using Shouldly;
using Xunit;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure
{
    public class StreamIdTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("s s")]
        public void When_invalid_then_should_throw(string value)
        {
            Action act = () => new StreamId(value);

            act.ShouldThrow<ArgumentException>();
        }

        [Fact]
        public void Is_equatable()
        {
            (new StreamId("foo") == new StreamId("foo")).ShouldBeTrue();
            new StreamId("foo").Equals(new StreamId("foo")).ShouldBeTrue();
            (new StreamId("foo") != new StreamId("bar")).ShouldBeTrue();
            new StreamId("foo").GetHashCode().ShouldBe(new StreamId("foo").GetHashCode());
        } 
    }
}