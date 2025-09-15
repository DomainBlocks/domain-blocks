namespace DomainBlocks.Examples.TodoList.Domain;

public class TodoItem(Guid id, string description)
{
    public Guid Id { get; } = id;
    public string Description { get; } = description;
    public TodoItemStatus Status { get; } = TodoItemStatus.Pending;
}