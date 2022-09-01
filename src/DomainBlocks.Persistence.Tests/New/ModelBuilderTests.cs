using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Persistence.New.Builders;
using NUnit.Framework;

namespace DomainBlocks.Persistence.Tests.New;

[TestFixture]
public class ModelBuilderTests
{
    [Test]
    public void HappyPathTest()
    {
        var model = new ModelBuilder()
            .Aggregate<PriceCaptureSession, IEvent>(aggregate =>
            {
                aggregate
                    .InitialState(() => new PriceCaptureSession())
                    .HasId(x => x.Id)
                    .WithStreamKey(id => $"priceCaptureSession-{id}")
                    .WithSnapshotKey(id => $"priceCaptureSessionSnapshot-{id}");

                aggregate
                    .CommandResult<IEnumerable<IEvent>>()
                    .WithEventsFrom((res, _) => res)
                    .WithUpdatedStateFrom((_, agg) => agg);

                aggregate
                    .CommandResult<CommandResult>()
                    .WithEventsFrom((res, _) => res.Events)
                    .WithUpdatedStateFrom((res, _) => res.UpdatedState);

                aggregate.ApplyEventsWith((agg, e) => agg.Apply(e));

                aggregate
                    .Event<PriceCaptureSessionStarted>()
                    .HasName(nameof(PriceCaptureSessionStarted));
            })
            .Build();

        var aggregateType = model.GetAggregateType<PriceCaptureSession, IEvent>();

        Assert.That(
            model.EventNameMap.GetEventName(typeof(PriceCaptureSessionStarted)),
            Is.EqualTo(nameof(PriceCaptureSessionStarted)));

        var commandResultType1 = aggregateType.GetCommandResultType<IEnumerable<IEvent>>();

        var priceCaptureSession = aggregateType.CreateNew();
        var commandResult = priceCaptureSession.Start().ToList();

        var (updatedState, events) = commandResultType1.GetUpdatedStateAndEvents(commandResult, priceCaptureSession);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        Assert.That(eventList[0], Is.TypeOf<PriceCaptureSessionStarted>());
        Assert.That(updatedState, Is.SameAs(priceCaptureSession));

        var commandResultType2 = aggregateType.GetCommandResultType<CommandResult>();
        var commandResult2 = priceCaptureSession.ImmutableStart();

        (updatedState, events) = commandResultType2.GetUpdatedStateAndEvents(commandResult2, priceCaptureSession);
        eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        Assert.That(eventList[0], Is.TypeOf<PriceCaptureSessionStarted>());
        Assert.That(updatedState, Is.SameAs(commandResult2.UpdatedState));
    }

    private interface IEvent
    {
    }

    private class PriceCaptureSessionStarted : IEvent
    {
        public string Id { get; init; }
    }

    private class CommandResult
    {
        public IEnumerable<IEvent> Events { get; init; }
        public PriceCaptureSession UpdatedState { get; init; }
    }

    private class PriceCaptureSession
    {
        public string Id { get; private set; }

        public IEnumerable<IEvent> Start()
        {
            var id = Guid.NewGuid().ToString();
            yield return new PriceCaptureSessionStarted { Id = id };
        }

        public CommandResult ImmutableStart()
        {
            var id = Guid.NewGuid().ToString();

            return new CommandResult
            {
                Events = new[] { new PriceCaptureSessionStarted { Id = id } },
                UpdatedState = this
            };
        }

        public void Apply(IEvent @event)
        {
            if (@event is PriceCaptureSessionStarted e)
            {
                Id = e.Id;
            }
        }
    }
}