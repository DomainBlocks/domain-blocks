namespace DomainBlocks.Core;

public enum MutableApplyEventsBehavior
{
    None,
    ApplyAfterEnumerating,
    ApplyWhileEnumerating
}