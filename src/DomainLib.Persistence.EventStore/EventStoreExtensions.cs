using EventStore.ClientAPI;
using System;

namespace DomainLib.Persistence.EventStore
{
    internal static class EventStoreExtensions
    {
        public static StreamEventsSlice ThrowIfNotSuccess(this StreamEventsSlice slice, string streamName)
        {
            switch (slice.Status)
            {
                case SliceReadStatus.Success:
                    return slice;
                case SliceReadStatus.StreamNotFound:
                    throw new StreamNotFoundException(streamName);
                case SliceReadStatus.StreamDeleted:
                    throw new StreamDeletedException(streamName);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}