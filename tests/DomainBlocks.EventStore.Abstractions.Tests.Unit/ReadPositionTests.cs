// using NUnit.Framework;
// using Shouldly;
//
// namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;
//
// public class ReadPositionTests
// {
//     [Test]
//     public void Instance_WhenDefaultConstructed_EqualsStart()
//     {
//         default(ReadPosition<StreamPosition>).ShouldBe(ReadPosition<StreamPosition>.Start);
//         new ReadPosition<StreamPosition>().ShouldBe(ReadPosition<StreamPosition>.Start);
//     }
//
//     [Test]
//     public void Version_WhenStartOrEnd_IsNull()
//     {
//         ReadPosition<StreamPosition>.Start.Specific.ShouldBeNull();
//         ReadPosition<StreamPosition>.End.Specific.ShouldBeNull();
//     }
//
//     [Test]
//     public void Version_WhenSpecific_IsSpecific()
//     {
//         var version = new StreamPosition(123);
//         var position = ReadPosition<StreamPosition>.At(version);
//         position.Specific.ShouldBe(version);
//     }
//
//     [Test]
//     public void ToString_WhenNonSpecific_ReturnsName()
//     {
//         ReadPosition<StreamPosition>.Start.ToString().ShouldBe("Start");
//         ReadPosition<StreamPosition>.End.ToString().ShouldBe("End");
//     }
//
//     [Test]
//     public void ToString_ForSpecific_ReturnsNumericValue()
//     {
//         var specific = ReadPosition<StreamPosition>.At(new StreamPosition(99));
//         specific.ToString().ShouldBe(new StreamPosition(99).ToString());
//     }
//
//     [Test]
//     public void Equals_WhenValuesAreSame_ReturnsTrue()
//     {
//         var a = ReadPosition<StreamPosition>.At(new StreamPosition(1));
//         var b = ReadPosition<StreamPosition>.At(new StreamPosition(1));
//
//         a.Equals(b).ShouldBeTrue();
//     }
//
//     [Test]
//     public void Equals_WhenValuesDiffer_ReturnsFalse()
//     {
//         var a = ReadPosition<StreamPosition>.At(new StreamPosition(1));
//         var b = ReadPosition<StreamPosition>.At(new StreamPosition(2));
//
//         a.Equals(b).ShouldBeFalse();
//         ReadPosition<StreamPosition>.Start.Equals(ReadPosition<StreamPosition>.End).ShouldBeFalse();
//         ReadPosition<StreamPosition>.Start.Equals(a).ShouldBeFalse();
//         ReadPosition<StreamPosition>.End.Equals(a).ShouldBeFalse();
//     }
// }