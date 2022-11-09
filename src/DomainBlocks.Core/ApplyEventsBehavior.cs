namespace DomainBlocks.Core;

public enum ApplyEventsBehavior
{
    DoNotApply,
    MaterializeFirst,
    ApplyWhileEnumerating
}