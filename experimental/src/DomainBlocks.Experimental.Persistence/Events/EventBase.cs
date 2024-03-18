namespace DomainBlocks.Experimental.Persistence.Events;

public abstract class EventBase
{
    private readonly BytesEventData? _bytesData;
    private readonly StringEventData? _stringData;

    protected EventBase(string name, BytesEventData bytesData)
    {
        Name = name;
        _bytesData = bytesData;
    }

    protected EventBase(string name, StringEventData stringData)
    {
        Name = name;
        _stringData = stringData;
    }

    public string Name { get; }
    public EventDataType DataType => _bytesData != null ? EventDataType.Bytes : EventDataType.String;

    public BytesEventData BytesData
    {
        get
        {
            if (_bytesData == null)
            {
                throw new InvalidOperationException();
            }

            return _bytesData;
        }
    }

    public StringEventData StringData
    {
        get
        {
            if (_stringData == null)
            {
                throw new InvalidOperationException();
            }

            return _stringData;
        }
    }
}