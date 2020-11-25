using System;
using System.Runtime.Serialization;

namespace DomainLib.Persistence
{
    [Serializable]
    public class SnapshotDoesNotExistException : Exception
    {
        public string SnapshotKey { get; }

        public SnapshotDoesNotExistException(string snapshotKey)
        {
            SnapshotKey = snapshotKey;
        }

        public SnapshotDoesNotExistException(string snapshotKey, string message) : base(message)
        {
            SnapshotKey = snapshotKey;
        }

        public SnapshotDoesNotExistException(string snapshotKey, string message, Exception inner) : base(message, inner)
        {
            SnapshotKey = snapshotKey;
        }

        protected SnapshotDoesNotExistException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            info.AddValue(nameof(SnapshotKey), SnapshotKey);

            base.GetObjectData(info, context);
        }
    }
}