using RakNexus.Core;

namespace RakNexus.Protocol;

public class InternalPacket
{
    public uint24 ReliableMessageNumber;
    public uint24 OrderingIndex;
    public byte OrderingChannel;
    
    public ushort SplitPacketId;
    public uint SplitPacketIndex;
    public uint SplitPacketCount;

    public int DataBitLength;
    public PacketReliability Reliability;
    public PacketPriority Priority;

    public byte[] Data = Array.Empty<byte>();

    public ulong CreationTime;
    public ulong NextActionTime;
    public byte TimesSent;
    public uint SendReceiptSerial;

    public int HeaderLength => CalculateHeaderLength();

    private int CalculateHeaderLength()
    {
        int bitLength = 8; // 3 bits reliability + 1 bit split + 4 bits padding
        bitLength += 16;   // Data bit length

        if (Reliability == PacketReliability.RELIABLE ||
            Reliability == PacketReliability.RELIABLE_SEQUENCED ||
            Reliability == PacketReliability.RELIABLE_ORDERED ||
            Reliability == PacketReliability.RELIABLE_WITH_ACK_RECEIPT ||
            Reliability == PacketReliability.RELIABLE_ORDERED_WITH_ACK_RECEIPT)
        {
            bitLength += 24;
        }

        if (Reliability == PacketReliability.UNRELIABLE_SEQUENCED ||
            Reliability == PacketReliability.RELIABLE_SEQUENCED ||
            Reliability == PacketReliability.RELIABLE_ORDERED ||
            Reliability == PacketReliability.RELIABLE_ORDERED_WITH_ACK_RECEIPT)
        {
            bitLength += 24; // Ordering Index
            bitLength += 8;  // Ordering Channel
        }

        if (SplitPacketCount > 0)
        {
            bitLength += 32; // Split packet count
            bitLength += 16; // Split packet ID
            bitLength += 32; // Split packet index
        }

        return bitLength;
    }
}