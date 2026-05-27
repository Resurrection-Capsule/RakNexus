using Xunit;
using RakNexus.Core;

namespace RakNexus.Tests;

/// <summary>
/// Wire-format regression tests for RakBitStream. The expected bytes are GROUND TRUTH,
/// captured from the real Darkspore client &lt;-&gt; dalkon's C++ darkspore_server (RakNet 3.902)
/// on 2026-05-27 (raknet_session.pcapng). Do NOT relax these to little-endian / 4-byte time:
/// past sessions broke interop by assuming host order. See memory raknexus-parity-vs-raknet392.
/// </summary>
public class BitStreamTests
{
    private static byte[] Bytes(RakBitStream bs)
    {
        int n = bs.GetNumberOfBytesUsed();
        var outp = new byte[n];
        System.Array.Copy(bs.GetData(), outp, n);
        return outp;
    }

    // ---- Bit-level primitives ----

    [Fact]
    public void Bit_MsbFirst_Alignment()
    {
        var bs = new RakBitStream();
        bs.Write(true);
        bs.Write(false);
        bs.AlignWriteToByteBoundary();
        Assert.Equal(0x80, bs.GetData()[0]); // first bit is MSB
    }

    [Theory]
    [InlineData(13)]   // not byte aligned
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(24)]
    public void WriteBits_ReadBits_RoundTrip(int numBits)
    {
        var src = new byte[] { 0xAB, 0xCD, 0xEF, 0x12 };
        var bs = new RakBitStream();
        bs.Write(true); // force a non-aligned start
        bs.WriteBits(src, numBits, true);

        var reader = new RakBitStream(Bytes(bs), bs.GetNumberOfBytesUsed(), false);
        Assert.True(reader.Read(out bool _));
        var dst = new byte[4];
        Assert.True(reader.ReadBits(dst, numBits, true));

        // compare the meaningful bits
        for (int i = 0; i < numBits / 8; i++)
            Assert.Equal(src[i], dst[i]);
    }

    // ---- Big-endian "user type" primitives (Write<T> on x86 swaps to network order) ----

    [Fact]
    public void UShort_IsBigEndian()
    {
        var bs = new RakBitStream();
        bs.Write((ushort)0x1234);
        Assert.Equal(new byte[] { 0x12, 0x34 }, Bytes(bs));
    }

    [Fact]
    public void UInt_IsBigEndian()
    {
        var bs = new RakBitStream();
        bs.Write(0x12345678u);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, Bytes(bs));
    }

    [Fact]
    public void ULong_IsBigEndian_TimestampGroundTruth()
    {
        // RakNetTime is 8 bytes (__GET_TIME_64BIT=1), big-endian on the wire.
        // Capture: serverTimestamp = 00 00 00 00 09 B1 27 AA.
        var bs = new RakBitStream();
        bs.Write(0x09B127AAul);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x09, 0xB1, 0x27, 0xAA }, Bytes(bs));

        var reader = new RakBitStream(Bytes(bs), bs.GetNumberOfBytesUsed(), false);
        Assert.True(reader.Read(out ulong v));
        Assert.Equal(0x09B127AAul, v);
    }

    [Fact]
    public void Float_RoundTrip()
    {
        var bs = new RakBitStream();
        bs.Write(3.14159f);
        var reader = new RakBitStream(Bytes(bs), bs.GetNumberOfBytesUsed(), false);
        Assert.True(reader.Read(out float v));
        Assert.Equal(3.14159f, v, 5);
    }

    // ---- uint24 (datagram number / range indices) is LITTLE-ENDIAN, byte-aligned ----

    [Fact]
    public void UInt24_IsLittleEndian()
    {
        var bs = new RakBitStream();
        bs.Write(new uint24(0x123456));
        Assert.Equal(new byte[] { 0x56, 0x34, 0x12 }, Bytes(bs));

        var reader = new RakBitStream(Bytes(bs), bs.GetNumberOfBytesUsed(), false);
        Assert.True(reader.Read(out uint24 v));
        Assert.Equal(0x123456u, v.Value);
    }

    // ---- SystemAddress: binaryAddress = ~addr (network order), port = big-endian ----

    [Fact]
    public void SystemAddress_GroundTruth_192_168_172_205_3659()
    {
        // Capture (CONNECTION_REQUEST_ACCEPTED recipient): 3F 57 53 32 (=~C0A8ACCD) + 0E 4B (=3659 BE).
        var addr = new SystemAddress(0xC0A8ACCD, 3659);
        var bs = new RakBitStream();
        bs.Write(addr);
        Assert.Equal(new byte[] { 0x3F, 0x57, 0x53, 0x32, 0x0E, 0x4B }, Bytes(bs));

        var reader = new RakBitStream(Bytes(bs), bs.GetNumberOfBytesUsed(), false);
        Assert.True(reader.Read(out SystemAddress back));
        Assert.Equal(0xC0A8ACCDu, back.BinaryAddress);
        Assert.Equal((ushort)3659, back.Port);
    }

    [Fact]
    public void SystemAddress_Unassigned_GroundTruth()
    {
        // Capture: internal IDs serialized as 00 00 00 00 FF FF (~0xFFFFFFFF + port 0xFFFF).
        var bs = new RakBitStream();
        bs.Write(SystemAddress.Unassigned);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF }, Bytes(bs));
    }

    // ---- RakNetGUID = Write<uint64_t> => big-endian ----

    [Fact]
    public void Guid_IsBigEndian_RoundTrip()
    {
        var bs = new RakBitStream();
        bs.Write(new RakNetGUID(0x0123456789ABCDEFul));
        Assert.Equal(new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF }, Bytes(bs));

        var reader = new RakBitStream(Bytes(bs), bs.GetNumberOfBytesUsed(), false);
        Assert.True(reader.Read(out RakNetGUID g));
        Assert.Equal(0x0123456789ABCDEFul, g.G);
    }

    // ---- WriteCompressed: strips zero bytes from the BIG-ENDIAN representation ----

    [Fact]
    public void Compressed_SmallValue_NotCompressed()
    {
        // 0x32 has a non-zero least-significant byte -> RakNet writes 1 flag bit + 32 bits = 33 bits.
        var bs = new RakBitStream();
        bs.WriteCompressed(0x32u);
        Assert.Equal(33, bs.GetNumberOfBitsUsed());
    }

    [Fact]
    public void Compressed_HighValue_Compressed()
    {
        // 0x32000000 has zero low bytes -> 3 flag bits + 1 flag + 8 bits = 12 bits.
        var bs = new RakBitStream();
        bs.WriteCompressed(0x32000000u);
        Assert.Equal(12, bs.GetNumberOfBitsUsed());
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(5u)]
    [InlineData(255u)]
    [InlineData(256u)]
    [InlineData(0x1234u)]
    [InlineData(0x32u)]
    [InlineData(0x32000000u)]
    [InlineData(0xDEADBEEFu)]
    [InlineData(0xFFFFFFFFu)]
    public void Compressed_RoundTrip(uint value)
    {
        var bs = new RakBitStream();
        bs.WriteCompressed(value);

        var reader = new RakBitStream(Bytes(bs), bs.GetNumberOfBytesUsed(), false);
        Assert.True(reader.ReadCompressed(out uint result));
        Assert.Equal(value, result);
    }

    // ---- Handshake message-size regression (locks the 8-byte timestamp fix) ----

    [Fact]
    public void ConnectionRequestAccepted_Is85Bytes()
    {
        // id(1) + recipient SystemAddress(6) + systemIndex(2) + 10x internal SystemAddress(60)
        // + requestTimestamp(8) + serverTimestamp(8) = 85. Matches C++ ground truth (was 77 with 4-byte time).
        var bs = new RakBitStream();
        bs.Write((byte)0x0E); // ID_CONNECTION_REQUEST_ACCEPTED
        bs.Write(new SystemAddress(0xC0A8ACCD, 55121));
        bs.WriteBits(System.BitConverter.GetBytes((ushort)0), 16, true);
        for (int i = 0; i < 10; i++) bs.Write(SystemAddress.Unassigned);
        bs.Write(0x09B127AAul);
        bs.Write(0x09B127AAul);
        Assert.Equal(85, bs.GetNumberOfBytesUsed());
    }
}
