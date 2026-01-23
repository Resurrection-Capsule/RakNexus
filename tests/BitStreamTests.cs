using Xunit;
using RakNexus.Core;

namespace RakNexus.Tests;

public class BitStreamTests
{
    [Fact]
    public void TestBitWriteAlignment()
    {
        var bs = new RakBitStream();
        bs.Write(true);
        bs.Write(false);
        bs.AlignWriteToByteBoundary();
        
        byte[] data = bs.GetData();
        Assert.Equal(0x80, data[0]); 
    }

    [Fact]
    public void TestCompressedUInt()
    {
        var bs = new RakBitStream();
        uint value = 5;
        bs.WriteCompressed(value);
        
        var reader = new RakBitStream(bs.GetData(), bs.GetNumberOfBytesUsed(), false);
        reader.ReadCompressed(out uint result);
        Assert.Equal(value, result);
    }

    [Fact]
    public void TestBigEndianParity()
    {
        var bs = new RakBitStream();
        ushort val = 0x1234;
        bs.Write(val);
        
        byte[] data = bs.GetData();
        Assert.Equal(0x12, data[0]);
        Assert.Equal(0x34, data[1]);
    }
}