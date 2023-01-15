namespace DomainBlocks.ThirdParty.SqlStreamStore.Streams
{
    public delegate Task<ListStreamsPage> ListNextStreamsPage(
        string continuationToken,
        CancellationToken cancellationToken = default);
}