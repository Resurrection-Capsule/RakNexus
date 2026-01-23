using Xunit;
using RakNexus.Core;
using RakNexus.Protocol;

namespace RakNexus.Tests;

public class StringCompressorTests
{
    [Fact]
    public void TestEnglishStringCompression()
    {
        var sc = StringCompressor.Instance;
        var bs = new RakBitStream();
        string input = "Hello World";
        
        sc.WriteCompressed(bs, input);
        
        var reader = new RakBitStream(bs.GetData(), bs.GetNumberOfBytesUsed(), false);
        bool success = sc.ReadCompressed(reader, out string output);
        
        Assert.True(success);
        Assert.Equal(input, output);
    }
}