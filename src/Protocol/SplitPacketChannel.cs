using RakNexus.Core;

namespace RakNexus.Protocol;

public partial class SplitPacketChannel
{
    public ulong LastUpdateTime;
    public SortedDictionary<uint, InternalPacket> SplitPackets = new();
    public uint SplitPacketsArrived;
    public InternalPacket? ReturnedPacket;
    
    public bool IsComplete => SplitPacketsArrived == ReturnedPacket?.SplitPacketCount;
}