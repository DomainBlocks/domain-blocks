namespace DomainBlocks.Core.Projections;

public interface IProjectionOptions
{
    ProjectionRegistry Register(ProjectionRegistry registry);
}