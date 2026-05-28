using Xunit;
using RakNexus.Core;
using RakNexus.Protocol;
using System.Text;

namespace RakNexus.Tests;

public class ReliabilityLayerTests
{
    // Builds a datagram exactly like the real send path (ProcessSendQueue):
    //   header byte 0x80 (isValid) [+0x08 continuousSend] + uint24 LE datagram number + InternalPacket.
    // This is what ProcessDatagram expects on the wire (NOT the old dead DatagramHeader struct).
    private static void Deliver(ReliabilityLayer rel, uint datagramNumber, InternalPacket packet,
        System.Action<InternalPacket> callback)
    {
        var bs = new RakBitStream();
        bs.Write((byte)0x80);
        bs.Write(new uint24(datagramNumber));
        rel.WriteInternalPacket(bs, packet);
        bs.ResetReadPointer();
        rel.ProcessDatagram(bs, 0, callback);
    }

    private static InternalPacket Split(ushort id, uint index, uint count, byte[] data, uint reliableMsgNum)
        => new InternalPacket
        {
            SplitPacketId = id,
            SplitPacketIndex = index,
            SplitPacketCount = count,
            Data = data,
            DataBitLength = data.Length * 8,
            Reliability = PacketReliability.RELIABLE,
            ReliableMessageNumber = reliableMsgNum
        };

    [Fact]
    public void OutOfOrderSplitPacketsReassembleCorrectly()
    {
        var rel = new ReliabilityLayer();
        const string rawData = "LARGE_DATA_RECAP_PACKET_SIMULATION";
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);

        var fragment1 = Split(100, 1, 2, bytes[10..], reliableMsgNum: 0);
        var fragment0 = Split(100, 0, 2, bytes[0..10], reliableMsgNum: 1);

        string? result = null;
        System.Action<InternalPacket> callback = p => result = Encoding.UTF8.GetString(p.Data);

        // Second fragment arrives first → nothing delivered yet.
        Deliver(rel, datagramNumber: 0, fragment1, callback);
        Assert.Null(result);

        // First fragment completes the set → reassembled in index order regardless of arrival order.
        Deliver(rel, datagramNumber: 1, fragment0, callback);
        Assert.Equal(rawData, result);
    }

    [Fact]
    public void InOrderSplitPacketsReassembleCorrectly()
    {
        var rel = new ReliabilityLayer();
        const string rawData = "ANOTHER_REASSEMBLY_PAYLOAD_CHECK";
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);

        string? result = null;
        System.Action<InternalPacket> callback = p => result = Encoding.UTF8.GetString(p.Data);

        Deliver(rel, 0, Split(7, 0, 2, bytes[0..16], 0), callback);
        Assert.Null(result);
        Deliver(rel, 1, Split(7, 1, 2, bytes[16..], 1), callback);
        Assert.Equal(rawData, result);
    }

    [Fact]
    public void ReliableOrderedPacketsDeliverInOrder()
    {
        var rel = new ReliabilityLayer();
        var delivered = new System.Collections.Generic.List<string>();
        System.Action<InternalPacket> callback = p => delivered.Add(Encoding.UTF8.GetString(p.Data));

        InternalPacket Ordered(uint orderingIndex, string s, uint msgNum) => new InternalPacket
        {
            Data = Encoding.UTF8.GetBytes(s),
            DataBitLength = Encoding.UTF8.GetByteCount(s) * 8,
            Reliability = PacketReliability.RELIABLE_ORDERED,
            OrderingChannel = 0,
            OrderingIndex = new uint24(orderingIndex),
            ReliableMessageNumber = msgNum
        };

        // Deliver ordering index 1 before 0 → must be buffered, then released in order.
        Deliver(rel, 0, Ordered(1, "SECOND", 0), callback);
        Assert.Empty(delivered);

        Deliver(rel, 1, Ordered(0, "FIRST", 1), callback);
        Assert.Equal(new[] { "FIRST", "SECOND" }, delivered);
    }

    [Fact]
    public void DuplicateDatagramIsIgnored()
    {
        var rel = new ReliabilityLayer();
        int count = 0;
        System.Action<InternalPacket> callback = _ => count++;

        var packet = new InternalPacket
        {
            Data = Encoding.UTF8.GetBytes("HELLO"),
            DataBitLength = 5 * 8,
            Reliability = PacketReliability.UNRELIABLE,
            ReliableMessageNumber = 0
        };

        Deliver(rel, 0, packet, callback);
        Deliver(rel, 0, packet, callback); // same datagram number → duplicate
        Assert.Equal(1, count);
    }

    [Fact]
    public void OversizedPacketIsFragmentedOnSend()
    {
        var rel = new ReliabilityLayer();
        byte[] big = new byte[5000]; // > MTU, like a LabsPlayerUpdate carrying the squad
        for (int i = 0; i < big.Length; i++) big[i] = (byte)(i & 0xFF);

        rel.EnqueueSend(new InternalPacket
        {
            Data = big,
            DataBitLength = big.Length * 8,
            Reliability = PacketReliability.UNRELIABLE_WITH_ACK_RECEIPT
        });

        var fragments = rel.sendQueue.GetPacketsToAssemble(int.MaxValue);
        Assert.True(fragments.Count > 1, "over-MTU packet must be split into multiple fragments");
        Assert.All(fragments, f => Assert.True(f.SplitPacketCount > 1));

        var reassembled = fragments.OrderBy(f => f.SplitPacketIndex).SelectMany(f => f.Data).ToArray();
        Assert.Equal(big, reassembled);
    }

    [Fact]
    public void SmallPacketIsNotFragmented()
    {
        var rel = new ReliabilityLayer();
        rel.EnqueueSend(new InternalPacket
        {
            Data = Encoding.UTF8.GetBytes("small"),
            DataBitLength = 5 * 8,
            Reliability = PacketReliability.UNRELIABLE_WITH_ACK_RECEIPT
        });

        var fragments = rel.sendQueue.GetPacketsToAssemble(int.MaxValue);
        Assert.Single(fragments);
        Assert.Equal(0u, fragments[0].SplitPacketCount);
    }
}
