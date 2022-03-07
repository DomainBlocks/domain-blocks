using DomainBlocks.Projections.AspNetCore;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static EntityFrameworkProjectionRegistrationBuilder<TDbContext> UseEntityFramework<TDbContext, TRawData>(
            this IProjectionRegistrationOptionsBuilderInfrastructure<TRawData> builder) where TDbContext : DbContext
        {
            var entityFrameworkProjectionBuilder = new EntityFrameworkProjectionRegistrationBuilder<TDbContext>();
            builder.UseProjectionRegistrations(entityFrameworkProjectionBuilder.Build());
            return entityFrameworkProjectionBuilder;
        }
    }
}
