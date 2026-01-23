using RakNexus.Core;
using System.Net;

namespace RakNexus.Protocol;

public class MessageFilter
{
    private readonly bool[] _allowedIds = new bool[256];
    private readonly Dictionary<EndPoint, int> _systemFilters = new();

    public MessageFilter()
    {
        AllowId(MessageId.ID_CONNECTION_REQUEST_ACCEPTED);
        AllowId(MessageId.ID_CONNECTION_ATTEMPT_FAILED);
        AllowId(MessageId.ID_ALREADY_CONNECTED);
        AllowId(MessageId.ID_NEW_INCOMING_CONNECTION);
        AllowId(MessageId.ID_NO_FREE_INCOMING_CONNECTIONS);
        AllowId(MessageId.ID_DISCONNECTION_NOTIFICATION);
        AllowId(MessageId.ID_CONNECTION_LOST);
        AllowId(MessageId.ID_OPEN_CONNECTION_REQUEST);
        AllowId(MessageId.ID_OPEN_CONNECTION_REPLY);
    }

    public void AllowId(MessageId id) => _allowedIds[(byte)id] = true;

    public bool IsAllowed(EndPoint remote, byte messageId)
    {
        return _allowedIds[messageId];
    }
}