namespace DomainBlocks.Examples.TodoList.Domain;

public class TodoListCreated(string name)
{
    public string Name { get; } = name;
}