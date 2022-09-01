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
        var modelBuilder = new ModelBuilder();

        modelBuilder
            .Aggregate<PriceCaptureSession>()
            .EventBaseType<IEvent>()
            .InitialState(() => new PriceCaptureSession())
            .HasId(x => x.Id)
            .WithStreamKey(id => $"priceCaptureSession-{id}")
            .WithSnapshotKey(id => $"priceCaptureSessionSnapshot-{id}")
            .HasCommandResult<IEnumerable<IEvent>>(result =>
            {
                result
                    .WithEventsFrom((_, res) => res)
                    .WithUpdatedStateFrom((agg, _) => agg);
            })
            .HasCommandResult<CommandResult>(result =>
            {
                result
                    .WithEventsFrom((_, res) => res.Events)
                    .WithUpdatedStateFrom((_, res) => res.UpdatedState);
            })
            .ApplyEventsWith((agg, e) =>
            {
                agg.Apply(e);
                return agg;
            });

        var model = modelBuilder.Build();

        var aggregateType = model.GetAggregateType<PriceCaptureSession, IEvent>();
        var commandResultType1 = aggregateType.GetCommandResultType<IEnumerable<IEvent>>();

        var priceCaptureSession = aggregateType.CreateNew();
        var commandResult = priceCaptureSession.Start().ToList();

        var events = commandResultType1.SelectEvents(priceCaptureSession, commandResult).ToList();
        var updatedState = commandResultType1.SelectUpdatedState(priceCaptureSession, commandResult);
        
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.TypeOf<PriceCaptureSessionStarted>());
        Assert.That(updatedState, Is.SameAs(priceCaptureSession));
        
        var commandResultType2 = aggregateType.GetCommandResultType<CommandResult>();
        var commandResult2 = priceCaptureSession.ImmutableStart();

        events = commandResultType2.SelectEvents(priceCaptureSession, commandResult2).ToList();
        updatedState = commandResultType2.SelectUpdatedState(priceCaptureSession, commandResult2);
        
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.TypeOf<PriceCaptureSessionStarted>());
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