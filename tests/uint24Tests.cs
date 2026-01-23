using Xunit;
using RakNexus.Core;

namespace RakNexus.Tests;

public class uint24Tests
{
    [Fact]
    public void TestWrappingOverflow()
    {
        uint24 max = 0x00FFFFFF;
        uint24 result = max + 1;
        Assert.Equal(0u, (uint)result);
    }

    [Fact]
    public void TestWrappingUnderflow()
    {
        uint24 min = 0;
        uint24 result = min - 1;
        Assert.Equal(0x00FFFFFFu, (uint)result);
    }

    [Fact]
    public void TestComparisonWithWrapping()
    {
        uint24 high = 0x00FFFFFE;
        uint24 zero = 0;
        Assert.True(zero > high == false);
        Assert.True((zero.Value - high.Value & 0x00FFFFFF) < 0x007FFFFF);
    }
}