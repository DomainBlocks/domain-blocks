using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Core;
using NSubstitute;

namespace DomainBlocks.Testing;

public static class Fakes
{
    private static readonly IList<Type> AllLibraryTypes;

    static Fakes()
    {
        BuildFakes();
        AllLibraryTypes = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => a.FullName!.Contains("DomainBlocks"))
            .SelectMany(a => a.GetTypes())
            .ToList();
    }

    public static IEventNameMap EventNameMap { get; private set; }

    private static void BuildFakes()
    {
        EventNameMap = BuildFakeEventNameMap();
    }

    private static IEventNameMap BuildFakeEventNameMap()
    {
        var substitute = Substitute.For<IEventNameMap>();
        substitute.GetEventName(Arg.Any<Type>()).Returns(args => ((Type)args[0]).Name);
        substitute.GetEventType(Arg.Any<string>())
            .Returns(args =>
            {
                var typeName = (string)args[0];
                var type = AllLibraryTypes.FirstOrDefault(t => t.Name == typeName);

                if (type == null)
                {
                    throw new InvalidOperationException($"Unable to find type with name {typeName}");
                }

                return type;
            });

        return substitute;
    }
}