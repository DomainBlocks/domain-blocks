using DomainBlocks.Serialization.GoogleProtobuf.Tests.Generated;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Serialization.GoogleProtobuf.Tests;

public class GoogleProtobufSerializerTest
{
    private readonly GoogleProtobufSerializer _serializer = new();

    [Test]
    public void Should_serialize_and_deserialize()
    {
        // Arrange
        var original = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        // Act
        var bytes = _serializer.Serialize(original);
        var deserialized = (UserCreated?)_serializer.Deserialize(bytes, typeof(UserCreated));

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.UserId.ShouldBe(original.UserId);
        deserialized.Name.ShouldBe(original.Name);
    }

    [Test]
    public void Serialize_Should_throw_when_value_is_not_IMessage()
    {
        var nonMessage = new { Id = 1 };

        Should.Throw<ArgumentException>(() => _serializer.Serialize(nonMessage));
    }

    [Test]
    public void Deserialize_should_throw_when_type_is_not_IMessage()
    {
        var bytes = "junk"u8.ToArray();

        Should.Throw<ArgumentException>(() => _serializer.Deserialize(bytes, typeof(string)));
    }

    [Test]
    public void Deserialize_should_throw_when_type_has_no_parser()
    {
        var bytes = "junk"u8.ToArray();

        Should.Throw<ArgumentException>(() => _serializer.Deserialize(bytes, typeof(FakeWithoutParser)));
    }

    [Test]
    public void Serialize_should_throw_when_value_is_null()
    {
        Should.Throw<ArgumentNullException>(() => _serializer.Serialize(null!));
    }

    [Test]
    public void Deserialize_should_throw_when_type_is_null()
    {
        var bytes = new byte[] { 0x01, 0x02 };

        Should.Throw<ArgumentNullException>(() => _serializer.Deserialize(bytes, null!));
    }

    // Dummy type without a Parser property to simulate error
    private sealed class FakeWithoutParser : IMessage<FakeWithoutParser>
    {
        public MessageDescriptor Descriptor => throw new NotImplementedException();
        public int CalculateSize() => throw new NotImplementedException();
        public FakeWithoutParser Clone() => throw new NotImplementedException();
        public bool Equals(FakeWithoutParser? other) => throw new NotImplementedException();
        public void MergeFrom(FakeWithoutParser message) => throw new NotImplementedException();
        public void MergeFrom(CodedInputStream input) => throw new NotImplementedException();
        public void WriteTo(CodedOutputStream output) => throw new NotImplementedException();
    }
}