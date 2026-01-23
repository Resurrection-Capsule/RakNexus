using RakNexus.Protocol;
using RakNexus.Core;

namespace RakNexus.Network;

public interface IRakPeer
{
    bool Startup(ushort maxConnections, int threadSleepTimer, int localPort);
    void Shutdown(uint blockDuration);
    
    uint Send(byte[] data, PacketPriority priority, PacketReliability reliability, byte orderingChannel, AddressOrGUID target, bool broadcast);
    
    void CloseConnection(SystemAddress target, bool sendNotification);
    
    int GetAveragePing(AddressOrGUID target);
    int GetLastPing(AddressOrGUID target);
    
    void SetOfflinePingResponse(byte[] data);
}