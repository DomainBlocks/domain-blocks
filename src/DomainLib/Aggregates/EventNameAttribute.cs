using System;

namespace DomainLib.Aggregates
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class EventNameAttribute : Attribute
    {
        public string EventName { get; }

        public EventNameAttribute(string eventName)
        {
            EventName = eventName;
        }
    }
}