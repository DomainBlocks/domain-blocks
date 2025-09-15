namespace DomainBlocks.Examples.TodoList.Domain;

public class TodoItemAdded(string listName, Guid itemId, string description)
{
    public string ListName { get; } = listName;
    public Guid ItemId { get; } = itemId;
    public string Description { get; } = description;
}