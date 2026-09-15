using System.Reflection;
using DomainBlocks.EventStore.Codecs;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Unit;

/// <summary>
/// The store sees only the codec contracts. Type mapping, contract mapping, metadata contribution and read transforms
/// are composed around the store, never passed into it. The package boundary that used to enforce this is gone, so
/// this test does.
/// </summary>
public class KurrentDBEventStoreDependencyTests
{
    private static readonly string[] PolicyNamespaces =
    [
        "DomainBlocks.EventStore.Metadata",
        "DomainBlocks.EventStore.Transforms",
        "DomainBlocks.EventStore.TypeMapping",
        "DomainBlocks.EventStore.ContractMapping"
    ];

    private static readonly Type[] AllowedCodecTypes = [typeof(IEventCodec<,,>), typeof(IEventEncoder<,,>), typeof(IEventDecoder<,,>)];

    [Test]
    public void ConstructionSurface_ParameterTypes_AreCodecContractsOrStoreTypes()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        var parameterTypes = typeof(KurrentDBEventStore<>).GetConstructors(all)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .SelectMany(Flatten)
            .Distinct()
            .ToArray();

        parameterTypes.ShouldNotBeEmpty();

        foreach (var type in parameterTypes)
        {
            PolicyNamespaces.ShouldNotContain(type.Namespace, $"{type} is domain policy and must not reach the store");

            if (type.Namespace == "DomainBlocks.EventStore.Codecs")
            {
                var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
                AllowedCodecTypes.ShouldContain(definition, $"{type} is not a codec contract");
            }
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.IsArray)
        {
            foreach (var t in Flatten(type.GetElementType()!))
                yield return t;
        }

        if (!type.IsGenericType)
            yield break;

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var t in Flatten(argument))
                yield return t;
        }
    }
}