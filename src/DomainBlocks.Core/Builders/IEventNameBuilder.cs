namespace DomainBlocks.Core.Builders;

public interface IEventNameBuilder
{
    /// <summary>
    /// Specify the name that the event is known by. Use this method if the name is different from the CLR type name.
    /// </summary>
    void HasName(string eventName);
}