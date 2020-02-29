# domain-lib

This library is intented to be a set of patterns and tools for use in projects that aim to apply concepts from DDD and event sourcing.

**The goals of this library are to:**
- Provide tools that are more functional in nature than the alternatives.
- Allow the domain layer to be kept clean of concerns that don't belong there.
- Avoid opinionated and limiting framework-esque design decisions such as concrete inheritance.

**What separates this from the myriad of similar libraries already out there?**

A lot of other similar libraries tend to be some variation of the [NEventStore.Domain](https://github.com/NEventStore/NEventStore.Domain) project. This is certainly not a critism of that project, and we all owe much to the concepts and patterns it has contributed to the community. However, there are areas in the design that domain-lib aims to improve upon.

For example, take the typical kind of interface seen for an aggregate root:

```
public interface IAggregate
{
    Guid Id { get; }
    int Version { get; }
    void ApplyEvent(object @event);
    ICollection GetUncommittedEvents();
    void ClearUncommittedEvents();
    IMemento GetSnapshot();
}
```

In order to use this functionality, aggregate roots must inherit from an abstract base class called `AggregateBase`. This introduces a number of concepts that, in our opinion, don't belong in the domain layer. For example all members of this interface, apart from `Id` and `ApplyEvent`, relate to concepts around persistance, which are concerns that really belong in the outer application and infrastructure layers. This interface is also opinionated around identity and versioning, along with the fact that an aggregate root must be mutable (i.e. `ApplyEvent` mutates the aggregate root's internal state).

The method `ClearUncommittedEvents` also poses an issue in the design. Generally speaking, there are two ways in which an aggregate root has events applied to it in order to update it's state:
1. Through handling commands. Aggregate roots handle commands, which apply business rules and enforce invariants, and change state by "emitting" events.
2. Through being rehydrated from the event log. A new aggregate root instance has historical events from the log applied to it to bring it up to the current state.

`ClearUncommittedEvents` only exists here as a workaround for the fact that, after the persistance layer hydrates the aggregate root from the event log, it will contain a list of the historical events in its uncommitted events collection. `ClearUncommittedEvents` exists so that the persistance layer can clear out those historical events so that they are not incorrectly appended to the event log again when the aggregate root is saved. This feels like a leaky abstraction.

We think there is a cleaner way to solve this problem which separates concerns, has fewer opinions, and allows for immutability. 
