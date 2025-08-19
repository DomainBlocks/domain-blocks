using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventSourcing.Tests.Unit;

public class TypeExtensionsTests
{
    [Test]
    public void ClosedTypes_ShouldBehaveLikeIsAssignableFrom()
    {
        // object <- string
        typeof(object).TryBindGenericParameters(typeof(string), out var results).ShouldBeTrue();
        results.ShouldBeEmpty();

        // string <- object
        typeof(string).TryBindGenericParameters(typeof(object), out results).ShouldBeFalse();
        results.ShouldBeNull();
    }

    [Test]
    public void OpenGenericType_ShouldMatchClosedType_AndBindParameters()
    {
        var openType = typeof(EntityBase<>);
        var closedType = typeof(MyEntity);

        var success = openType.TryBindGenericParameters(closedType, out var bindings);

        success.ShouldBeTrue();
        bindings.ShouldNotBeNull();
        bindings.Count.ShouldBe(1);
        bindings.ShouldContainKey(openType.GetGenericArguments()[0]);
        bindings[openType.GetGenericArguments()[0]].ShouldBe(typeof(MyState));
    }

    [Test]
    public void OpenGenericType_ShouldNotBindIncompatibleClosedType()
    {
        var openType = typeof(EntityBase<>);
        var closedType = typeof(string);

        var success = openType.TryBindGenericParameters(closedType, out var bindings);

        success.ShouldBeFalse();
        bindings.ShouldBeNull();
    }

    [Test]
    public void ComplexGenericType_ShouldBindAllParameters()
    {
        var openType = typeof(ComplexBase<,,>);
        var closedType = typeof(ComplexEntity);

        var success = openType.TryBindGenericParameters(closedType, out var bindings);

        success.ShouldBeTrue();
        bindings.ShouldNotBeNull();
        bindings.Count.ShouldBe(3);

        var genericArgs = openType.GetGenericArguments();

        bindings[genericArgs[0]].ShouldBe(typeof(int));
        bindings[genericArgs[1]].ShouldBe(typeof(string));
        bindings[genericArgs[2]].ShouldBe(typeof(List<string>));
    }

    // ReSharper disable UnusedTypeParameter

    private class StateBase<T> where T : StateBase<T>;

    private class MyState : StateBase<MyState>;

    private class EntityBase<TState> where TState : StateBase<TState>;

    private class MyEntity : EntityBase<MyState>;

    private class ComplexBase<TKey, TValue, TCollection> where TCollection : ICollection<TValue>;

    private class ComplexEntity : ComplexBase<int, string, List<string>>;
}