using Xunit;
using RakNexus.Core;
using RakNexus.Protocol;
using System.Text;

namespace RakNexus.Tests;

public class ReliabilityLayerTests
{
    [Fact]
    public void TestOut考fOrderReassembly()
    {
        var rel = new ReliabilityLayer();
        string rawData = "LARGE_DATA_RECAP_PACKET_SIMULATION";
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);

        var p1 = new InternalPacket {
            SplitPacketId = 100, SplitPacketIndex = 1, SplitPacketCount = 2,
            Data = bytes[10..], DataBitLength = (bytes.Length - 10) * 8,
            Reliability = PacketReliability.RELIABLE,
            ReliableMessageNumber = 0 
        };
        
        var p0 = new InternalPacket {
            SplitPacketId = 100, SplitPacketIndex = 0, SplitPacketCount = 2,
            Data = bytes[0..10], DataBitLength = 10 * 8,
            Reliability = PacketReliability.RELIABLE,
            ReliableMessageNumber = 1
        };

        string? result = null;
        Action<InternalPacket> callback = (p) => {
            result = Encoding.UTF8.GetString(p.Data);
        };

        void SimulateNetworkReceive(InternalPacket packet)
        {
            var bs = new RakBitStream();
            
            var header = new DatagramHeader
            {
                IsValid = true,
                DatagramNumber = new uint24((uint)packet.ReliableMessageNumber.Value)
            };
            header.Serialize(bs);
            rel.WriteInternalPacket(bs, packet);
            bs.ResetReadPointer(); 
            rel.ProcessDatagram(bs, 0, callback);
        }

        SimulateNetworkReceive(p1);
        Assert.Null(result); 

        SimulateNetworkReceive(p0);
        Assert.Equal(rawData, result);
    }
}