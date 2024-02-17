namespace DomainBlocks.Experimental.EventSourcing;

public interface IDeepCloneable<out T>
{
    T Clone();
}