namespace RakNexus.Protocol;

public static class FrameFlags
{
    public const byte RELIABILITY_OFFSET = 5;
    public const byte RELIABILITY_MASK = 0xE0; // 1110 0000
    public const byte SPLIT_FLAG_BIT = 0x10;    // 0001 0000

    public static byte GetReliability(byte flags) => (byte)((flags & RELIABILITY_MASK) >> RELIABILITY_OFFSET);
    public static bool IsSplit(byte flags) => (flags & SPLIT_FLAG_BIT) != 0;
    
    public static byte Pack(PacketReliability reliability, bool isSplit)
    {
        byte b = (byte)((byte)reliability << RELIABILITY_OFFSET);
        if (isSplit) b |= SPLIT_FLAG_BIT;
        return b;
    }
}