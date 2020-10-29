using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DomainLib.Persistence;
using DomainLib.Serialization;
using NUnit.Framework;

namespace DomainLib.Tests.Serialization
{
    [TestFixture]
    public class MetadataSerializationTests
    {
        private const string TestServiceName = "TestService";
        private const string StaticKey = "StaticKey";
        private const string StaticValue = "StaticValue";
        private const string LazyKey = "LazyKey";
        private const string LazyValue = "LazyValue";

        [Test]
        public void DefaultMetadataIsIncludedInEventData()
        {
            var serializer = CreateJsonSerializerWithMetadata();
            var @event = new TestEvent("Some Value");
            var persistenceData = serializer.GetPersistenceData(@event);

            var metadata = GetMetadataFromEventPersistenceData(persistenceData);

            Assert.That(metadata.Keys.Count, Is.EqualTo(4));
            AssertDefaultMetadataIsPresent(metadata);
        }

        [Test]
        public void StaticEntriesCanBeAddedToMetadata()
        {
            var serializer = CreateJsonSerializerWithMetadata(context =>
            {
                context.AddEntry(StaticKey, StaticValue);
            });

            var @event = new TestEvent("Some Value");
            var persistenceData = serializer.GetPersistenceData(@event);

            var metadata = GetMetadataFromEventPersistenceData(persistenceData);

            Assert.That(metadata.Keys.Count, Is.EqualTo(5));
            AssertDefaultMetadataIsPresent(metadata);

            Assert.That(metadata.ContainsKey(StaticKey));
            Assert.That(metadata[StaticKey], Is.EqualTo(StaticValue));
        }

        [Test]
        public void LazyEntriesCanBeAddedToMetadata()
        {
            Func<string> lazyValueFunc = () => LazyValue;

            var serializer = CreateJsonSerializerWithMetadata(context =>
            {
                context.AddEntry(LazyKey, lazyValueFunc);
            });

            var @event = new TestEvent("Some Value");
            var persistenceData = serializer.GetPersistenceData(@event);

            var metadata = GetMetadataFromEventPersistenceData(persistenceData);

            Assert.That(metadata.Keys.Count, Is.EqualTo(5));
            AssertDefaultMetadataIsPresent(metadata);

            Assert.That(metadata.ContainsKey(LazyKey));
            Assert.That(metadata[LazyKey], Is.EqualTo(LazyValue));
        }

        [Test]
        [TestCaseSource(typeof(DuplicateKeysData), nameof(DuplicateKeysData.Cases))]
        public bool CannotHaveDuplicateKeys(string[] staticKeys, string[] lazyKeys)
        {
            try
            {
                CreateJsonSerializerWithMetadata(context =>
                {
                    foreach (var key in staticKeys)
                    {
                        context.AddEntry(key, "SomeValue");
                    }

                    foreach (var key in lazyKeys)
                    {
                        context.AddEntry(key, () => "LazyValue");
                    }
                });
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            return true;
        }

        private class DuplicateKeysData
        {
            public static IEnumerable Cases
            {
                get
                {
                    // All keys unique, should not throw and return true
                    yield return new TestCaseData(new[] {"Key1", "Key2"}, new[] {"Key3", "Key4"}).Returns(true);
                    // Static keys the same. Should throw and return false
                    yield return new TestCaseData(new[] {"Key1", "Key1"}, new[] {"Key3", "Key4"}).Returns(false);
                    // Lazy keys the same. Should throw and return false
                    yield return new TestCaseData(new[] {"Key1", "Key2"}, new[] {"Key3", "Key3"}).Returns(false);
                    // Lazy key the same as static key. Should throw and return false
                    yield return new TestCaseData(new[] {"Key1", "Key2"}, new[] {"Key2", "Key4"}).Returns(false);
                }
            }
        }

        [Test]
        public void SerializationWorksWhenNoMetadataContext()
        {
            var serializer = new JsonEventSerializer();

            var @event = new TestEvent("Some Value");
            var persistenceData = serializer.GetPersistenceData(@event);

            Assert.That(persistenceData.EventMetadata, Is.Null);
        }

        [Test]
        public void EmptyMetaDataContextDoesNotIncludeDefaults()
        {
            var emptyMetadataContext = EventMetadataContext.CreateEmpty();
            var serializer = new JsonEventSerializer();
            serializer.UseMetaDataContext(emptyMetadataContext);

            emptyMetadataContext.AddEntry(StaticKey, StaticValue);

            var @event = new TestEvent("Some Value");
            var persistenceData = serializer.GetPersistenceData(@event);

            var metadata = GetMetadataFromEventPersistenceData(persistenceData);

            Assert.That(metadata.Keys.Count, Is.EqualTo(1));
            Assert.That(metadata.ContainsKey(StaticKey));
            Assert.That(metadata[StaticKey], Is.EqualTo(StaticValue));
        }

        private static void AssertDefaultMetadataIsPresent(IDictionary<string, string> metadata)
        {
            Assert.That(metadata.ContainsKey(MetadataKeys.LoggedInUserName));
            Assert.That(metadata.ContainsKey(MetadataKeys.MachineName));
            Assert.That(metadata.ContainsKey(MetadataKeys.ServiceName));
            Assert.That(metadata.ContainsKey(MetadataKeys.UtcDateTime));

            Assert.That(metadata[MetadataKeys.LoggedInUserName], Is.EqualTo(Environment.UserName));
            Assert.That(metadata[MetadataKeys.MachineName], Is.EqualTo(Environment.MachineName));
            Assert.That(metadata[MetadataKeys.ServiceName], Is.EqualTo(TestServiceName));
            Assert.That(metadata[MetadataKeys.UtcDateTime], Is.Not.Null);
        }

        private static IEventSerializer CreateJsonSerializerWithMetadata(Action<EventMetadataContext> applyCustomMetadata = null)
        {
            var metadataContext = EventMetadataContext.CreateWithDefaults(TestServiceName);
            var serializer = new JsonEventSerializer();
            serializer.UseMetaDataContext(metadataContext);

            applyCustomMetadata?.Invoke(metadataContext);

            return serializer;
        }

        private static IDictionary<string, string> GetMetadataFromEventPersistenceData(IEventPersistenceData persistenceData)
        {
            return JsonSerializer.Deserialize<List<KeyValuePair<string, string>>>(persistenceData.EventMetadata)
                                 .ToDictionary(x => x.Key, x => x.Value);
        }

        public class TestEvent
        {
            public TestEvent(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }
    }

}