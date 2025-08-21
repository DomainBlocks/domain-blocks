namespace DomainBlocks.Examples.TodoList.Domain;

public class TotoItemCompleted(string listName, Guid itemId)
{
    public string ListName { get; } = listName;
    public Guid ItemId { get; } = itemId;
}