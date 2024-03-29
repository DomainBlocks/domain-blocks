﻿using System.Linq;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;

namespace SqlStreamStore
{
    using System;
    using System.Threading.Tasks;
    using Shouldly;
    using Xunit;

    public partial class AcceptanceTests
    {
        [Fact, Trait("Category", "DeleteStream")]
        public async Task When_delete_stream_with_no_expected_version_and_read_then_should_get_StreamNotFound()
        {
            const string streamId = "stream";

            await Store.AppendToStream(streamId, ExpectedVersion.NoStream, CreateNewStreamMessages(1, 2, 3));
            await Store.DeleteStream(streamId);

            var page =
                await Store.ReadStreamForwards(streamId, StreamVersion.Start, 10);

            page.Status.ShouldBe(PageReadStatus.StreamNotFound);
        }

        [Fact, Trait("Category", "DeleteStream")]
        public async Task When_delete_stream_with_expected_version_any_and_then_read_then_should_stream_deleted_message()
        {
            const string streamId = "stream";

            await Store.AppendToStream(streamId, ExpectedVersion.NoStream, CreateNewStreamMessages(1, 2, 3));
            await Store.DeleteStream(streamId, 2);

            var page =
                await Store.ReadStreamBackwards(Deleted.DeletedStreamId, StreamVersion.End, 1);

            page.Status.ShouldBe(PageReadStatus.Success);
            var message = page.Messages.Single();
            message.Type.ShouldBe(Deleted.StreamDeletedMessageType);
            var streamDeleted = await message.GetJsonDataAs<Deleted.StreamDeleted>();
            streamDeleted.StreamId.ShouldBe("stream");
        }

        [Fact, Trait("Category", "DeleteStream")]
        public async Task When_delete_stream_with_no_expected_version_and_read_all_then_should_not_see_deleted_stream_messages()
        {
            const string streamId = "stream";

            await Store.AppendToStream(streamId, ExpectedVersion.NoStream, CreateNewStreamMessages(1, 2, 3));
            await Store.DeleteStream(streamId);

            var page = await Store.ReadAllForwards(Position.Start, 10);

            page.Messages.Any(message => message.StreamId == streamId).ShouldBeFalse();
        }

        [Fact, Trait("Category", "DeleteStream")]
        public void When_delete_stream_that_does_not_exist()
        {
            const string streamId = "notexist";
            Func<Task> act = () => Store.DeleteStream(streamId);

            act.ShouldNotThrow();
        }

        [Fact, Trait("Category", "DeleteStream")]
        public async Task When_delete_stream_with_a_matching_expected_version_and_read_then_should_get_StreamNotFound()
        {
            const string streamId = "stream";

            await Store.AppendToStream(streamId, ExpectedVersion.NoStream, CreateNewStreamMessages(1, 2, 3));
            await Store.DeleteStream(streamId, 2);

            var page = await Store.ReadStreamForwards(streamId, StreamVersion.Start, 10);

            page.Status.ShouldBe(PageReadStatus.StreamNotFound);
        }

        [Fact, Trait("Category", "DeleteStream")]
        public async Task When_delete_stream_with_a_matching_expected_version_and_read_then_should_get_stream_deleted_message()
        {
            const string streamId = "stream";

            await Store.AppendToStream(streamId, ExpectedVersion.NoStream, CreateNewStreamMessages(1, 2, 3));
            await Store.DeleteStream(streamId, 2);

            var page = await Store.ReadStreamBackwards(Deleted.DeletedStreamId, StreamVersion.End, 1);

            page.Status.ShouldBe(PageReadStatus.Success);
            var message = page.Messages.Single();
            message.Type.ShouldBe(Deleted.StreamDeletedMessageType);
            var streamDeleted = await message.GetJsonDataAs<Deleted.StreamDeleted>();
            streamDeleted.StreamId.ShouldBe("stream");
        }

        [Fact, Trait("Category", "DeleteStream")]
        public async Task When_delete_stream_with_a_matching_expected_version_and_read_all_then_should_not_see_deleted_stream_messages()
        {
            const string streamId = "stream";

            await Store.AppendToStream(streamId, ExpectedVersion.NoStream, CreateNewStreamMessages(1, 2, 3));
            await Store.DeleteStream(streamId);

            var allMessagesPage = await Store.ReadAllForwards(Position.Start, 10);

            allMessagesPage.Messages.Any(message => message.StreamId == streamId).ShouldBeFalse();
        }

        [Fact, Trait("Category", "DeleteStream")]
        public async Task When_delete_stream_that_does_not_exist_then_should_not_throw()
        {
            const string streamId = "notexist";

            var exception = await Record.ExceptionAsync(() => Store.DeleteStream(streamId));

            exception.ShouldBeNull();
        }

        [Fact, Trait("Category", "DeleteStream")]
        public async Task When_delete_stream_that_does_not_exist_with_expected_version_number_then_should_not_throw()
        {
            const string streamId = "notexist";
            const int expectedVersion = 1;

            var exception = await Record.ExceptionAsync(() =>
                Store.DeleteStream(streamId, expectedVersion));

            exception.ShouldBeOfType<WrongExpectedVersionException>(
                ErrorMessages.DeleteStreamFailedWrongExpectedVersion(streamId, expectedVersion));
        }

        [Fact, Trait("Category", "DeleteStream")]
        public async Task When_delete_stream_with_a_non_matching_expected_version_then_should_throw()
        {
            const string streamId = "stream";
            await Store.AppendToStream(streamId, ExpectedVersion.NoStream, CreateNewStreamMessages(1, 2, 3));

            var exception = await Record.ExceptionAsync(() =>
                Store.DeleteStream(streamId, 100));

            exception.ShouldBeOfType<WrongExpectedVersionException>(
                    ErrorMessages.DeleteStreamFailedWrongExpectedVersion(streamId, 100));
        }

        [Theory, Trait("Category", "DeleteStream")]
        [InlineData("stream/id")]
        [InlineData("stream%id")]
        public async Task When_delete_stream_with_url_encodable_characters_then_should_not_throw(string streamId)
        {
            var newStreamMessages = CreateNewStreamMessages(1);
            await Store.AppendToStream(streamId, ExpectedVersion.NoStream, newStreamMessages);

            await Store.DeleteStream(streamId);
        }
    }
}
