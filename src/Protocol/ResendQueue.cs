using RakNexus.Core;

namespace RakNexus.Protocol;

public class ResendQueue
{
    private const int BUFFER_SIZE = 512;
    private const int BUFFER_MASK = 511;

    private readonly InternalPacket?[] _buffer = new InternalPacket[BUFFER_SIZE];
    private readonly LinkedList<InternalPacket> _resendList = new();
    
    public uint UnacknowledgedBytes { get; private set; }

    public void Add(InternalPacket packet, ulong time, ulong rto)
    {
        uint index = packet.ReliableMessageNumber.Value & BUFFER_MASK;
        if (_buffer[index] != null) 
            throw new InvalidOperationException("Resend buffer overflow. Peer unacked count > 512.");

        packet.NextActionTime = time + rto;
        packet.TimesSent = 1;

        _buffer[index] = packet;
        _resendList.AddLast(packet);
        UnacknowledgedBytes += (uint)packet.Data.Length + (uint)(packet.HeaderLength / 8);
    }

    public void OnAck(uint24 messageNumber, out InternalPacket? ackedPacket)
    {
        uint index = messageNumber.Value & BUFFER_MASK;
        ackedPacket = _buffer[index];

        if (ackedPacket != null && ackedPacket.ReliableMessageNumber == messageNumber)
        {
            _buffer[index] = null;
            _resendList.Remove(ackedPacket);
            UnacknowledgedBytes -= (uint)ackedPacket.Data.Length + (uint)(ackedPacket.HeaderLength / 8);
        }
    }

    public void OnNak(uint24 messageNumber, ulong curTime)
    {
        uint index = messageNumber.Value & BUFFER_MASK;
        var packet = _buffer[index];
        if (packet != null && packet.ReliableMessageNumber == messageNumber)
        {
            packet.NextActionTime = curTime;
        }
    }

    public IEnumerable<InternalPacket> GetExpired(ulong curTime)
    {
        return _resendList.Where(p => p.NextActionTime <= curTime);
    }
}