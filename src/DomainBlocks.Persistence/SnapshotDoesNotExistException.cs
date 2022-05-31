using System;
using System.Runtime.Serialization;

namespace DomainBlocks.Persistence;

[Serializable]
public class SnapshotDoesNotExistException : Exception
{
    public SnapshotDoesNotExistException(string snapshotKey) :
        this(snapshotKey, $"A snapshot with key '{snapshotKey}' does not exist")
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

    protected SnapshotDoesNotExistException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        SnapshotKey = (string)info.GetValue(nameof(SnapshotKey), typeof(string));
    }
    
    public string SnapshotKey { get; }
    
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(SnapshotKey), SnapshotKey);
    }
}