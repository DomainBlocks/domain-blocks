namespace DomainBlocks.Examples.TodoList.Domain;

public class TodoItemCompleted(string listName, Guid itemId)
{
    public string ListName { get; } = listName;
    public Guid ItemId { get; } = itemId;
}