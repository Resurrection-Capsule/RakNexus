using System.Net;
using RakNexus.Core;

namespace RakNexus.Network;

public enum ConnectMode
{
    NO_ACTION,
    DISCONNECT_ASAP,
    DISCONNECT_ASAP_SILENTLY,
    DISCONNECT_ON_NO_ACK,
    REQUESTED_CONNECTION,
    HANDLING_CONNECTION_REQUEST,
    UNVERIFIED_SENDER,
    CONNECTED
}

public class RemoteSystemState
{
    public bool IsActive;
    public SystemAddress SystemAddress;
    public RakNetGUID Guid;
    public int MTUSize = RakConstants.MAXIMUM_MTU_SIZE;
    public ConnectMode ConnectMode = ConnectMode.NO_ACTION;
    
    public ulong ConnectionTime;
    public ulong LastReliableSend;
    public uint LowestPing = 65535;

    public SystemAddress[] TheirInternalIds = new SystemAddress[RakConstants.MAXIMUM_NUMBER_OF_INTERNAL_IDS];
}