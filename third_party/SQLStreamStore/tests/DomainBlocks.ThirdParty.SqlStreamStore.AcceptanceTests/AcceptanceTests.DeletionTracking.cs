using DomainBlocks.ThirdParty.SqlStreamStore.Streams;

namespace SqlStreamStore
{
    using System.Threading.Tasks;
    using Shouldly;
    using Xunit;

    partial class AcceptanceTests
    {
        [Fact]
        public async Task When_deletion_tracking_is_disabled_deleted_message_should_not_be_tracked()
        {
            Fixture.DisableDeletionTracking = true;

            var messages = CreateNewStreamMessages(1);
            await Store.AppendToStream("stream", ExpectedVersion.NoStream, messages);
            await Store.DeleteMessage("stream", messages[0].MessageId);
            var page = await Store.ReadStreamBackwards(Deleted.DeletedStreamId, StreamVersion.End, 1);

            page.Messages.Length.ShouldBe(0);
        }

        [Fact]
        public async Task When_deletion_tracking_is_disabled_deleted_stream_should_not_be_tracked()
        {
            Fixture.DisableDeletionTracking = true;
            
            var messages = CreateNewStreamMessages(1);
            await Store.AppendToStream("stream", ExpectedVersion.NoStream, messages);
            await Store.DeleteStream("stream");
            var page = await Store.ReadStreamBackwards(Deleted.DeletedStreamId, StreamVersion.End, 1);

            page.Messages.Length.ShouldBe(0);
        }
    }
}