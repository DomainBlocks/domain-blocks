namespace DomainBlocks.Examples.TodoList.Domain;

public class TodoList
{
    public string Name { get; private set; } = string.Empty;
    private List<TodoItem> _items = [];

    public TodoList()
    {
    }

    public TodoList(string name)
    {
    }

    public void AddItem(string description)
    {
    }

    public void CompleteItem(Guid itemId)
    {
    }

    public void Archive()
    {
    }
}