﻿using System;
using System.Linq;
using DomainBlocks.Core.Builders;
using NUnit.Framework;

namespace DomainBlocks.Core.Tests.Builders;

[TestFixture]
public class AssemblyEventTypeBuilderTests
{
    [Test]
    public void BuildFindsRelevantEventTypes()
    {
        var builder = new AssemblyEventTypeBuilder<IEvent1>(typeof(IEvent1).Assembly);
        builder.FilterByBaseType<IFilterType1>();
        var eventTypes = ((IEventTypeBuilder)builder).Build().ToList();

        Assert.That(eventTypes, Has.Count.EqualTo(2));
        Assert.That(eventTypes[0].ClrType, Is.EqualTo(typeof(Event1)));
        Assert.That(eventTypes[0].EventName, Is.EqualTo(nameof(Event1)));
        Assert.That(eventTypes[1].ClrType, Is.EqualTo(typeof(Event2)));
        Assert.That(eventTypes[1].EventName, Is.EqualTo(nameof(Event2)));
    }
    
    [Test]
    public void BuiltThrowsExceptionWhenNoEventsFound()
    {
        var builder = new AssemblyEventTypeBuilder<IEvent2>(typeof(IEvent2).Assembly);
        Assert.Throws<InvalidOperationException>(() => ((IEventTypeBuilder)builder).Build());
    }

    [Test]
    public void BuiltThrowsExceptionWhenNoEventsWithFilterBaseTypeFound()
    {
        var builder = new AssemblyEventTypeBuilder<IEvent1>(typeof(IEvent1).Assembly);
        builder.FilterByBaseType<IFilterType2>();
        Assert.Throws<InvalidOperationException>(() => ((IEventTypeBuilder)builder).Build());
    }

    private interface IEvent1
    {
    }

    private interface IFilterType1
    {
    }

    private class Event1 : IEvent1, IFilterType1
    {
    }

    private class Event2 : IEvent1, IFilterType1
    {
    }

    // Abstract class should be ignored
    public abstract class Event3 : IEvent1, IFilterType1
    {
    }

    // Event type without filtering base type should be ignored
    private class Event4 : IEvent1
    {
    }
    
    private interface IEvent2
    {
    }
    
    private interface IFilterType2
    {
    }
}