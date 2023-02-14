namespace DomainBlocks.Core.Builders;

public interface ITypeFilterBuilder
{
    /// <summary>
    /// Specify an additional filter to use on the event types which have been found in the specified assembly. The
    /// argument of the predicate is a type derived from the specified base event type.
    /// </summary>
    void Where(Func<Type, bool> predicate);
}