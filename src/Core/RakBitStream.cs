using System.Runtime.InteropServices;
using RakNexus.Core;

namespace RakNexus.Core;

public class RakBitStream
{
    private byte[] _data;
    private int _numberOfBitsUsed;
    private int _numberOfBitsAllocated;
    private int _readOffset;
    private bool _copyData;

    private const int STACK_ALLOC_SIZE = 256;

    public RakBitStream() : this(STACK_ALLOC_SIZE) { }

    public RakBitStream(int initialBytesToAllocate)
    {
        _numberOfBitsUsed = 0;
        _readOffset = 0;
        _numberOfBitsAllocated = initialBytesToAllocate * 8;
        _data = new byte[initialBytesToAllocate];
        _copyData = true;
    }

    public RakBitStream(byte[] data, int lengthInBytes, bool copyData)
    {
        _numberOfBitsUsed = lengthInBytes * 8;
        _readOffset = 0;
        _copyData = copyData;
        _numberOfBitsAllocated = lengthInBytes * 8;

        if (_copyData)
        {
            _data = new byte[lengthInBytes];
            if (data != null) Buffer.BlockCopy(data, 0, _data, 0, lengthInBytes);
        }
        else
        {
            _data = data ?? Array.Empty<byte>();
        }
    }

    public void Reset()
    {
        _numberOfBitsUsed = 0;
        _readOffset = 0;
    }

    public void ResetReadPointer()
    {
        _readOffset = 0;
    }

    // --- Core Bit IO ---

    public void Write(bool value)
    {
        AddBitsAndReallocate(1);
        int numberOfBitsMod8 = _numberOfBitsUsed & 7;
        
        if (numberOfBitsMod8 == 0)
        {
            _data[_numberOfBitsUsed >> 3] = (byte)(value ? 0x80 : 0x00);
        }
        else
        {
            if (value) _data[_numberOfBitsUsed >> 3] |= (byte)(0x80 >> numberOfBitsMod8);
        }
        _numberOfBitsUsed++;
    }

    public bool Read(out bool value)
    {
        if (_readOffset + 1 > _numberOfBitsUsed) { value = false; return false; }
        value = (_data[_readOffset >> 3] & (0x80 >> (_readOffset & 7))) != 0;
        _readOffset++;
        return true;
    }

    public void WriteBits(byte[] input, int numberOfBitsToWrite, bool rightAlignedBits = true)
    {
        if (numberOfBitsToWrite <= 0) return;
        AddBitsAndReallocate(numberOfBitsToWrite);

        int numberOfBitsUsedMod8 = _numberOfBitsUsed & 7;
        
        if (numberOfBitsUsedMod8 == 0 && (numberOfBitsToWrite & 7) == 0)
        {
            Buffer.BlockCopy(input, 0, _data, _numberOfBitsUsed >> 3, numberOfBitsToWrite >> 3);
            _numberOfBitsUsed += numberOfBitsToWrite;
            return;
        }

        int inputIdx = 0;
        int bitsProcessing = numberOfBitsToWrite;
        
        while (bitsProcessing > 0)
        {
            byte dataByte = input[inputIdx++];
            if (bitsProcessing < 8 && rightAlignedBits) dataByte <<= (8 - bitsProcessing);

            if (numberOfBitsUsedMod8 == 0) _data[_numberOfBitsUsed >> 3] = dataByte;
            else
            {
                _data[_numberOfBitsUsed >> 3] |= (byte)(dataByte >> numberOfBitsUsedMod8);
                if (8 - numberOfBitsUsedMod8 < 8 && 8 - numberOfBitsUsedMod8 < bitsProcessing)
                    _data[(_numberOfBitsUsed >> 3) + 1] = (byte)(dataByte << (8 - numberOfBitsUsedMod8));
            }

            if (bitsProcessing >= 8) { _numberOfBitsUsed += 8; bitsProcessing -= 8; }
            else { _numberOfBitsUsed += bitsProcessing; bitsProcessing = 0; }
        }
    }
    
    public byte PeekByte()
    {
        if (_readOffset + 8 > _numberOfBitsUsed) return 0;
        int oldReadOffset = _readOffset;
        byte b;
        ReadBits(out b, 8);
        _readOffset = oldReadOffset;
        return b;
    }

    private void ReadBits(out byte b, int numberOfBits)
    {
        byte[] outB = new byte[1];
        ReadBits(outB, numberOfBits, true);
        b = outB[0];
    }
    public bool ReadBits(byte[] output, int numberOfBitsToRead, bool alignBitsToRight = true)
    {
        if (numberOfBitsToRead <= 0 || _readOffset + numberOfBitsToRead > _numberOfBitsUsed) return false;

        int readOffsetMod8 = _readOffset & 7;
        if (readOffsetMod8 == 0 && (numberOfBitsToRead & 7) == 0)
        {
            Buffer.BlockCopy(_data, _readOffset >> 3, output, 0, numberOfBitsToRead >> 3);
            _readOffset += numberOfBitsToRead;
            return true;
        }

        int offset = 0;
        int bitsProcessing = numberOfBitsToRead;
        Array.Clear(output, 0, (bitsProcessing + 7) >> 3);

        while (bitsProcessing > 0)
        {
            output[offset] |= (byte)(_data[_readOffset >> 3] << readOffsetMod8);
            if (readOffsetMod8 > 0 && bitsProcessing > (8 - readOffsetMod8))
                output[offset] |= (byte)(_data[(_readOffset >> 3) + 1] >> (8 - readOffsetMod8));

            if (bitsProcessing >= 8) { bitsProcessing -= 8; _readOffset += 8; offset++; }
            else
            {
                int neg = bitsProcessing - 8;
                if (neg < 0 && alignBitsToRight) { output[offset] >>= -neg; _readOffset += (8 + neg); }
                else { _readOffset += 8; }
                offset++;
                bitsProcessing = 0;
            }
        }
        return true;
    }

    // --- RakNet Compression ---
    
    public void WriteCompressed(uint value)
    {
        byte[] input = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian) Array.Reverse(input); 

        for (int i = 3; i > 0; i--)
        {
            if (input[i] == 0) Write(true);
            else
            {
                Write(false);
                byte[] remaining = new byte[i + 1];
                Array.Copy(input, 0, remaining, 0, i + 1);
                WriteBits(remaining, (i + 1) * 8, true);
                return;
            }
        }
        byte lastByte = input[0];
        if ((lastByte & 0xF0) == 0x00) { Write(true); WriteBits(new byte[] { lastByte }, 4, true); }
        else { Write(false); WriteBits(new byte[] { lastByte }, 8, true); }
    }

    public bool ReadCompressed(out uint value)
    {
        value = 0;
        byte[] buffer = new byte[4];
        
        for (int i = 3; i > 0; i--)
        {
            bool isZero;
            if (!Read(out isZero)) return false;
            if (isZero) buffer[i] = 0;
            else
            {
                int bits = (i + 1) * 8;
                byte[] temp = new byte[(bits+7)/8];
                if (!ReadBits(temp, bits, true)) return false;
                Array.Copy(temp, 0, buffer, 0, i + 1);
                value = BitConverter.ToUInt32(buffer, 0); 
                return true;
            }
        }
        bool isHalfZero;
        if (!Read(out isHalfZero)) return false;
        byte[] b = new byte[1];
        if (isHalfZero) { if (!ReadBits(b, 4, true)) return false; buffer[0] = b[0]; }
        else { if (!ReadBits(b, 8, true)) return false; buffer[0] = b[0]; }
        
        value = BitConverter.ToUInt32(buffer, 0);
        return true;
    }

    // --- Types Helpers ---

    public void Write(byte val) => WriteBits(new byte[] { val }, 8);
    public bool Read(out byte val) { byte[] b = new byte[1]; bool r = ReadBits(b, 8); val = b[0]; return r; }

    public void Write(ushort val) // Network Order (BE) for user types
    {
        byte[] b = BitConverter.GetBytes(val);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        WriteBits(b, 16);
    }
    public bool Read(out ushort val)
    {
        byte[] b = new byte[2];
        if (ReadBits(b, 16)) { if (BitConverter.IsLittleEndian) Array.Reverse(b); val = BitConverter.ToUInt16(b, 0); return true; }
        val = 0; return false;
    }

    public void Write(uint val) // Network Order (BE)
    {
        byte[] b = BitConverter.GetBytes(val);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        WriteBits(b, 32);
    }
    public bool Read(out uint val)
    {
        byte[] b = new byte[4];
        if (ReadBits(b, 32)) { if (BitConverter.IsLittleEndian) Array.Reverse(b); val = BitConverter.ToUInt32(b, 0); return true; }
        val = 0; return false;
    }
    
    public void WriteLE(ushort val)
    {
        AlignWriteToByteBoundary();
        byte[] b = BitConverter.GetBytes(val);
        if (!BitConverter.IsLittleEndian) Array.Reverse(b);
        WriteBits(b, 16);
    }
    public void WriteLE(uint val)
    {
        AlignWriteToByteBoundary();
        byte[] b = BitConverter.GetBytes(val);
        if (!BitConverter.IsLittleEndian) Array.Reverse(b);
        WriteBits(b, 32);
    }
    public bool ReadLE(out ushort val)
    {
        AlignReadToByteBoundary();
        byte[] b = new byte[2];
        if (ReadBits(b, 16)) { if (!BitConverter.IsLittleEndian) Array.Reverse(b); val = BitConverter.ToUInt16(b, 0); return true; }
        val = 0; return false;
    }
    public bool ReadLE(out uint val)
    {
        AlignReadToByteBoundary();
        byte[] b = new byte[4];
        if (ReadBits(b, 32)) { if (!BitConverter.IsLittleEndian) Array.Reverse(b); val = BitConverter.ToUInt32(b, 0); return true; }
        val = 0; return false;
    }

    public void Write(float val)
    {
        byte[] b = BitConverter.GetBytes(val);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        WriteBits(b, 32);
    }
    public bool Read(out float val)
    {
        byte[] b = new byte[4];
        if (ReadBits(b, 32)) { if (BitConverter.IsLittleEndian) Array.Reverse(b); val = BitConverter.ToSingle(b, 0); return true; }
        val = 0; return false;
    }
    
    public void Write(uint24 val)
    {
        AlignWriteToByteBoundary();
        byte[] b = BitConverter.GetBytes(val.Value);
        WriteBits(b, 24, true); 
    }
    public bool Read(out uint24 val)
    {
        AlignReadToByteBoundary();
        byte[] b = new byte[4];
        if (ReadBits(b, 24)) { val = new uint24(BitConverter.ToUInt32(b, 0)); return true; }
        val = 0; return false;
    }

    // Extensions
    public void SetWriteOffset(int offset) => _numberOfBitsUsed = offset;
    public void AlignReadToByteBoundary() { if (_readOffset > 0) _readOffset += (8 - (((_readOffset - 1) & 7) + 1)); }
    public void AlignWriteToByteBoundary() { if (_numberOfBitsUsed > 0) _numberOfBitsUsed += (8 - (((_numberOfBitsUsed - 1) & 7) + 1)); }
    
    public void WriteAlignedBytes(byte[] input)
    {
        AlignWriteToByteBoundary();
        WriteBits(input, input.Length * 8, true);
    }
    
    public byte[] GetData() => _data;
    public int GetNumberOfBytesUsed() => (_numberOfBitsUsed + 7) >> 3;
    public int GetNumberOfBitsUsed() => _numberOfBitsUsed;
    public int GetNumberOfUnreadBits() => _numberOfBitsUsed - _readOffset;

    private void AddBitsAndReallocate(int bits)
    {
        int newBits = _numberOfBitsUsed + bits;
        if (newBits > _numberOfBitsAllocated)
        {
            int newSize = ((newBits - 1) / 8) + 1;
            if (newSize < 256) newSize = 256;
            newSize *= 2;
            Array.Resize(ref _data, newSize);
            _numberOfBitsAllocated = newSize * 8;
        }
    }

    public void Write(RakNetGUID guid)
    {
        WriteNativeOrder(guid.G);
    }
    
    public void WriteNativeOrder(ulong val)
    {
        byte[] b = BitConverter.GetBytes(val);
        WriteBits(b, 64);
    }
    public bool Read(out RakNetGUID guid)
    {
        if (ReadNativeOrder(out ulong g))
        {
            guid = new RakNetGUID(g);
            return true;
        }
        guid = RakNetGUID.Unassigned;
        return false;
    }
    
    public bool ReadNativeOrder(out ulong val)
    {
        byte[] b = new byte[8];
        if (ReadBits(b, 64)) 
        { 
            val = BitConverter.ToUInt64(b, 0); 
            return true; 
        }
        val = 0; 
        return false;
    }

    public void WriteNativeOrder(uint val)
    {
        byte[] b = BitConverter.GetBytes(val);
        WriteBits(b, 32);
    }

    public bool ReadNativeOrder(out uint val)
    {
        byte[] b = new byte[4];
        if (ReadBits(b, 32)) 
        { 
            val = BitConverter.ToUInt32(b, 0); 
            return true; 
        }
        val = 0; 
        return false;
    }

    public void Write(SystemAddress addr)
    {
        byte octet0 = (byte)((addr.BinaryAddress >> 24) & 0xFF);
        byte octet1 = (byte)((addr.BinaryAddress >> 16) & 0xFF);
        byte octet2 = (byte)((addr.BinaryAddress >> 8) & 0xFF);
        byte octet3 = (byte)(addr.BinaryAddress & 0xFF);
        
        byte[] hiddenBytes = new byte[4];
        hiddenBytes[0] = (byte)~octet0;
        hiddenBytes[1] = (byte)~octet1;
        hiddenBytes[2] = (byte)~octet2;
        hiddenBytes[3] = (byte)~octet3;
        
        WriteBits(hiddenBytes, 32, true);
        
        byte[] portBytes = BitConverter.GetBytes(addr.Port);
        WriteBits(portBytes, 16, true);
    }
    
    public bool Read(out SystemAddress addr)
    {
        addr = SystemAddress.Unassigned;
        
        byte[] hiddenBytes = new byte[4];
        if (!ReadBits(hiddenBytes, 32, true)) return false;
        
        byte octet0 = (byte)~hiddenBytes[0];
        byte octet1 = (byte)~hiddenBytes[1];
        byte octet2 = (byte)~hiddenBytes[2];
        byte octet3 = (byte)~hiddenBytes[3];
        
        uint binary = ((uint)octet0 << 24) | ((uint)octet1 << 16) | ((uint)octet2 << 8) | octet3;
        
        byte[] portBytes = new byte[2];
        if (!ReadBits(portBytes, 16, true)) return false;
        ushort port = BitConverter.ToUInt16(portBytes, 0);
        
        addr = new SystemAddress(binary, port);
        return true;
    }
    
    public void Write(ulong val)
    {
        byte[] b = BitConverter.GetBytes(val);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        WriteBits(b, 64);
    }
    public bool Read(out ulong val)
    {
        byte[] b = new byte[8];
        if (ReadBits(b, 64)) 
        { 
            if (BitConverter.IsLittleEndian) Array.Reverse(b); 
            val = BitConverter.ToUInt64(b, 0); 
            return true; 
        }
        val = 0; return false;
    }

    public void IgnoreBytes(int bytes)
    {
        _readOffset += bytes * 8;
        if (_readOffset > _numberOfBitsUsed) _readOffset = _numberOfBitsUsed;
    }

    public int GetReadOffset() => _readOffset;
    public byte[] GetDataCopy() => (byte[])_data.Clone();
}