using RakNexus.Core;

namespace RakNexus.Protocol;

public struct DatagramHeader
{
    public uint24 DatagramNumber;
    public float AS; 
    public bool IsACK;
    public bool IsNAK;
    public bool IsPacketPair;
    public bool HasBAndAS;
    public bool IsContinuousSend;
    public bool NeedsBAndAs;
    public bool IsValid;
    public uint SourceSystemTime;

    public void Serialize(RakBitStream bs)
    {
        if (IsACK)
        {
            bs.Write(true);
            bs.Write(HasBAndAS);
            bs.AlignWriteToByteBoundary();
            bs.Write(SourceSystemTime);
            if (HasBAndAS) bs.Write(AS);
        }
        else if (IsNAK)
        {
            bs.Write(false);
            bs.Write(true);
        }
        else
        {
            bs.Write(false);
            bs.Write(false);
            bs.Write(IsPacketPair);
            bs.Write(IsContinuousSend);
            bs.Write(NeedsBAndAs);
            bs.AlignWriteToByteBoundary();
            bs.Write(SourceSystemTime);
            bs.Write(DatagramNumber);
        }
    }

    public static DatagramHeader Deserialize(RakBitStream bs)
    {
        var h = new DatagramHeader();
        
        if (bs.GetNumberOfUnreadBits() < 8) 
        {
            h.IsValid = false;
            return h;
        }
        h.IsValid = true;

        bs.AlignReadToByteBoundary();
        byte headerByte;
        
        if (!bs.Read(out headerByte)) 
        { 
            h.IsValid = false; 
            return h; 
        }

        if ((headerByte & 0x80) == 0)
        {
            h.IsACK = false;
            h.IsNAK = false;
            h.IsPacketPair = false; 
            h.DatagramNumber = 0;
            h.SourceSystemTime = 0;
            return h;
        }

        if ((headerByte & 0x40) != 0)
        {
            h.IsACK = true;
            h.IsNAK = false;
            h.IsPacketPair = false;
            h.HasBAndAS = false;
            h.SourceSystemTime = 0; 
            
            return h;
        }
        
        if ((headerByte & 0x20) != 0)
        {
            h.IsACK = false;
            h.IsNAK = true;
            h.IsPacketPair = false;
            h.SourceSystemTime = 0;
            return h;
        }

        {
            h.IsACK = false;
            h.IsNAK = false;
            h.IsPacketPair = false;
            byte b2, b3;
            bs.Read(out b2);
            bs.Read(out b3);
            h.DatagramNumber = (uint24)((uint)(headerByte | (b2 << 8) | (b3 << 16)));
            bs.Read(out h.SourceSystemTime);
            return h;
        }
    }
}