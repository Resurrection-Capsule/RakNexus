using Xunit;
using RakNexus.Core;
using RakNexus.Protocol;

namespace RakNexus.Tests;

public class RangeListTests
{
    [Fact]
    public void TestRangeMerging()
    {
        var rl = new RangeList<uint24>();
        rl.Insert(10);
        rl.Insert(11);
        rl.Insert(12);
        rl.Insert(14);
        
        Assert.Equal(2, rl.Ranges.Count);
        Assert.Equal(10u, rl.Ranges[0].MinIndex.Value);
        Assert.Equal(12u, rl.Ranges[0].MaxIndex.Value);
        Assert.Equal(14u, rl.Ranges[1].MinIndex.Value);
    }

    [Fact]
    public void TestSerializationCompatibility()
    {
        var rl = new RangeList<uint24>();
        rl.Insert(100);
        
        var bs = new RakBitStream();
        rl.Serialize(bs);
        
        var reader = new RakBitStream(bs.GetData(), bs.GetNumberOfBytesUsed(), false);
        var decoded = new RangeList<uint24>();
        bool success = decoded.Deserialize(reader);
        
        Assert.True(success);
        Assert.Single(decoded.Ranges);
        Assert.Equal(100u, decoded.Ranges[0].MinIndex.Value);
    }
}