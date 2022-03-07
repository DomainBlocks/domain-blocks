using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Serialization
{
    public static class MetadataKeys
    {
        public const string LoggedInUserName = "LoggedInUserName";
        public const string MachineName = "MachineName";
        public const string ServiceName = "ServiceName";
        public const string UtcDateTime = "UtcDataTime";
    }

    public sealed class EventMetadataContext
    {
        private readonly Dictionary<string, string> _metaDataEntries = new Dictionary<string, string>();
        private readonly Dictionary<string, Func<string>> _lazyEntries = new Dictionary<string, Func<string>>();

        private EventMetadataContext()
        {
        }

        private EventMetadataContext(string loggedInUserName, string machineName, string serviceName)
        {
            if (loggedInUserName == null) throw new ArgumentNullException(nameof(loggedInUserName));
            if (machineName == null) throw new ArgumentNullException(nameof(machineName));
            if (serviceName == null) throw new ArgumentNullException(nameof(serviceName));

            AddEntry(MetadataKeys.LoggedInUserName, loggedInUserName);
            AddEntry(MetadataKeys.MachineName, machineName);
            AddEntry(MetadataKeys.ServiceName, serviceName);
            AddEntry(MetadataKeys.UtcDateTime, () => DateTime.UtcNow.ToString("O"));
        }

        public static EventMetadataContext CreateEmpty()
        {
            return new EventMetadataContext();
        }

        public static EventMetadataContext CreateWithDefaults(string serviceName)
        {
            var loggedInUserName = Environment.UserName;
            var machineName = Environment.MachineName;
  
            return new EventMetadataContext(loggedInUserName, machineName, serviceName);
        }

        public void AddEntry(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (_metaDataEntries.ContainsKey(key) || _lazyEntries.ContainsKey(key))
            {
                throw new InvalidOperationException($"There is already a metadata entry with key {key}");
            }

            _metaDataEntries.Add(key, value);
        }

        public void AddEntry(string key, Func<string> valueFunc)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (valueFunc == null) throw new ArgumentNullException(nameof(valueFunc));
            if (_metaDataEntries.ContainsKey(key) || _lazyEntries.ContainsKey(key))
            {
                throw new InvalidOperationException($"There is already a metadata entry with key {key}");
            }

            _lazyEntries.Add(key, valueFunc);
        }

        public EventMetadata BuildMetadata(params KeyValuePair<string, string>[] additionalEntries)
        {
            return EventMetadata.FromKeyValuePairs(_metaDataEntries
                                                   .Concat(EvaluateLazyEntries())
                                                   .Concat(additionalEntries));
        }

        private IEnumerable<KeyValuePair<string, string>> EvaluateLazyEntries()
        {
            foreach (var kvp in _lazyEntries)
            {
                yield return KeyValuePair.Create(kvp.Key, kvp.Value());
            }
        }
    }
}