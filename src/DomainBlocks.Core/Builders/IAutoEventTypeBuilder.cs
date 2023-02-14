namespace DomainBlocks.Core.Builders;

public interface IAutoEventTypeBuilder : IMethodVisibilityBuilder
{
    /// <summary>
    /// Specify the name of the event applier method overloads on the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IMethodVisibilityBuilder WithMethodName(string methodName);
}