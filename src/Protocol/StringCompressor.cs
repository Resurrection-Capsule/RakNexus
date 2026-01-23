using RakNexus.Core;

namespace RakNexus.Protocol;

public class StringCompressor
{
    private static readonly Lazy<StringCompressor> _instance = new(() => new StringCompressor());
    public static StringCompressor Instance => _instance.Value;

    private readonly HuffmanEncodingTree _defaultTree;

    private StringCompressor()
    {
        _defaultTree = new HuffmanEncodingTree();
    }

    public void WriteCompressed(RakBitStream bs, string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            bs.WriteCompressed(0u);
            return;
        }

        var tempBs = new RakBitStream();
        _defaultTree.Encode(input, tempBs);
        
        uint bitLength = (uint)tempBs.GetNumberOfBitsUsed();
        bs.WriteCompressed(bitLength);
        
        byte[] compressedData = tempBs.GetData();
        bs.WriteBits(compressedData, (int)bitLength, false); 
    }

    public bool ReadCompressed(RakBitStream bs, out string result)
    {
        result = string.Empty;
        if (!bs.ReadCompressed(out uint bitLength)) return false;
        if (bitLength == 0) return true;

        result = _defaultTree.Decode(bs, (int)bitLength);
        return true;
    }
}