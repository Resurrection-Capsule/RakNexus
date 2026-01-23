using RakNexus.Core;
using RakNexus.Protocol;

namespace RakNexus.Diagnostics;

public class PacketLogger
{
    public void OnInternalPacket(InternalPacket packet, byte[] rawData, bool isSend)
    {
        string dir = isSend ? "SND" : "RCV";
        byte messageId = 0;
        
        if (packet.Data != null && packet.Data.Length > 0)
        {
            messageId = packet.Data[0];
        }
        
        string idName = GetIdName(messageId);
        
        Console.WriteLine($"[{dir}] ID: {idName} ({messageId}) | Size: {packet.DataBitLength} bits | Seq: {packet.ReliableMessageNumber}");
        
        if (packet.SplitPacketCount > 0)
        {
            Console.WriteLine($"    FRAGMENT {packet.SplitPacketIndex + 1}/{packet.SplitPacketCount} (SplitID: {packet.SplitPacketId})");
        }
    }

    private string GetIdName(byte id)
    {
        if (id >= (byte)MessageId.ID_USER_PACKET_ENUM) 
            return $"GAME_DATA_0x{id:X2}";
            
        return Enum.IsDefined(typeof(MessageId), id) 
            ? ((MessageId)id).ToString() 
            : $"UNKNOWN_0x{id:X2}";
    }
}