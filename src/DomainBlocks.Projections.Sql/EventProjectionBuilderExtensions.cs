namespace DomainBlocks.Projections.Sql
{
    public static class EventProjectionBuilderExtensions
    {
        public static SqlProjectionBuilder<TEvent, TSqlProjection> ToSqlProjection<TEvent, TSqlProjection>(
            this EventProjectionBuilder<TEvent> builder,
            TSqlProjection projection) where TSqlProjection : ISqlProjection
        {
            return new(builder, projection);
        }
    }
}