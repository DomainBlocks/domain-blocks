namespace DomainBlocks.Core.Builders;

public interface IMethodVisibilityBuilder
{
    /// <summary>
    /// Specify to include non-public event applier methods.
    /// </summary>
    void IncludeNonPublicMethods();
}