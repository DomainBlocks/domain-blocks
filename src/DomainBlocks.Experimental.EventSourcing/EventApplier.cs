using System.Linq.Expressions;
using System.Reflection;

namespace DomainBlocks.Experimental.EventSourcing;

public static class EventApplier
{
    public static EventApplier<TState> Create<TState, TEvent>(Func<TState, TEvent, TState> applier)
    {
        if (applier == null) throw new ArgumentNullException(nameof(applier));

        return new EventApplier<TState>(typeof(TEvent), (s, e) => applier(s, (TEvent)e));
    }

    public static EventApplier<TState> Create<TState, TEvent>(Action<TState, TEvent> applier)
    {
        if (applier == null) throw new ArgumentNullException(nameof(applier));

        return Create<TState, TEvent>((s, e) =>
        {
            applier(s, e);
            return s;
        });
    }

    internal static EventApplier<TState> Create<TState>(MethodInfo applierMethod)
    {
        var stateType = typeof(TState);
        var parameters = applierMethod.GetParameters();

        if (applierMethod.IsStatic)
        {
            throw new ArgumentException("Static event applier methods are not supported.", nameof(applierMethod));
        }

        if (!stateType.IsAssignableFrom(applierMethod.DeclaringType))
        {
            throw new ArgumentException(
                $"Expected declaring type of method to be '{stateType}', but got '{applierMethod.DeclaringType}'.",
                nameof(applierMethod));
        }

        if (parameters.Length != 1)
        {
            throw new ArgumentException("Expected method with a single argument.", nameof(applierMethod));
        }

        if (applierMethod.ReturnType != typeof(void) && applierMethod.ReturnType != stateType)
        {
            throw new ArgumentException(
                $"Expected method to return void or type '{stateType}'.", nameof(applierMethod));
        }

        var eventType = parameters[0].ParameterType;
        var stateParam = Expression.Parameter(stateType, "state");
        var eventParam = Expression.Parameter(typeof(object), "event");
        var call = Expression.Call(stateParam, applierMethod, Expression.Convert(eventParam, eventType));
        Expression body;

        if (applierMethod.ReturnType == typeof(void))
        {
            // Create an expression to return the state parameter.
            var returnLabel = Expression.Label(stateType, "returnState");
            var returnExpression = Expression.Return(returnLabel, stateParam, stateType);
            var labelExpression = Expression.Label(returnLabel, Expression.Default(stateType));

            // Combine the call and return expressions.
            body = Expression.Block(call, returnExpression, labelExpression);
        }
        else
        {
            body = call;
        }

        var lambda = Expression.Lambda<Func<TState, object, TState>>(body, stateParam, eventParam);
        var eventApplierFunc = lambda.Compile();
        return new EventApplier<TState>(eventType, eventApplierFunc);
    }
}

public sealed class EventApplier<TState>
{
    private readonly Func<TState, object, TState> _applierFunc;

    internal EventApplier(Type eventType, Func<TState, object, TState> applierFunc)
    {
        EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
        _applierFunc = applierFunc ?? throw new ArgumentNullException(nameof(applierFunc));
    }

    public Type EventType { get; }

    public TState Invoke(TState state, object @event)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        try
        {
            return _applierFunc(state, @event);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Error invoking event applier function for event type '{@event.GetType()}'.", ex);
        }
    }
}