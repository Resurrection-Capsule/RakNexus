using RakNexus.Core;

namespace RakNexus.Protocol;

public class Packet
{
    public SystemAddress SystemAddress;
    public RakNetGUID Guid;
    public int Length;
    public int BitSize;
    public byte[] Data;
    public byte PacketId => Data != null && Data.Length > 0 ? Data[0] : (byte)0;

    public Packet(byte[] data, int length, SystemAddress systemAddress, RakNetGUID guid)
    {
        Data = new byte[length];
        Buffer.BlockCopy(data, 0, Data, 0, length);
        Length = length;
        BitSize = length * 8;
        SystemAddress = systemAddress;
        Guid = guid;
    }
}