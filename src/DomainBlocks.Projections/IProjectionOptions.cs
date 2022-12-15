namespace DomainBlocks.Projections;

public interface IProjectionOptions
{
    ProjectionRegistry Register(ProjectionRegistry registry);
}