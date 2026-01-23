using RakNexus.Core;

namespace RakNexus.Protocol;

public partial class ReliabilityLayer
{
    private readonly OrderingChannel[] _orderingChannels = new OrderingChannel[32];
    private readonly Dictionary<ushort, SplitPacketChannel> _splitChannels = new();
    private readonly object _syncLock = new(); 
    public readonly CongestionManager congestionManager = new();
    public readonly ResendQueue resendQueue = new();
    public readonly SendQueue sendQueue = new();
    public readonly RangeList<uint24> naks = new();
    public readonly RangeList<uint24> acks = new();    
    private readonly Dictionary<uint24, uint24> _datagramToMessage = new();

    private ulong _timeLastDatagramArrived;
    private uint _timeoutTime = 10000; 
    private ulong _lastUpdateTime;

    private const int DATAGRAM_HISTORY_SIZE = 512;
    private uint24 _receivedPacketsBaseIndex = 0;
    private readonly bool[] _hasReceivedPacketQueue = new bool[DATAGRAM_HISTORY_SIZE];

    public uint TimeoutTime => _timeoutTime;

    private uint24 _nextDatagramNumber = 0;
    private uint24 _nextReliableMessageNumber = 0;
    private ushort _nextSplitPacketId = 0;
    private ushort GetNextSplitPacketId() => _nextSplitPacketId++;
    public readonly RakNetStatistics Statistics = new();
    private uint24 GetNextDatagramNumber() => _nextDatagramNumber++;
    private uint24 GetNextReliableMessageNumber() => _nextReliableMessageNumber++;

    public ReliabilityLayer()
    {
        for (int i = 0; i < 32; i++) _orderingChannels[i] = new OrderingChannel();
        congestionManager.Init(RakTime.GetTimeUS(), RakConstants.MAXIMUM_MTU_SIZE - RakConstants.UDP_HEADER_SIZE);
        for(int i=0; i<DATAGRAM_HISTORY_SIZE; i++) _hasReceivedPacketQueue[i] = false;
        _timeLastDatagramArrived = RakTime.GetTimeMS();
        _lastUpdateTime = RakTime.GetTimeUS();
    }

    public void SetTimeoutTime(uint ms) => _timeoutTime = ms;

    public bool IsDeadConnection(ulong curTimeMS)
    {
        lock (_syncLock)
        {
            if (resendQueue.UnacknowledgedBytes > 0 && curTimeMS - _timeLastDatagramArrived > _timeoutTime)
                return true;
        }
        return false;
    }

    public void OnDatagramArrived(ulong curTimeMS) => _timeLastDatagramArrived = curTimeMS;

    public void Update(ulong curTimeUS, Action<byte[], int> transmitAction)
    {
        lock (_syncLock)
        {
            ulong timeSinceLastTick = curTimeUS - _lastUpdateTime;
            _lastUpdateTime = curTimeUS;
            
            if (acks.Ranges.Count > 0)
            {
                Console.WriteLine($"[ReliabilityLayer] UPDATE: Sending ACKs ({acks.Ranges.Count} ranges)");
                SendAcks(transmitAction);
                congestionManager.ResetOldestAck();
            }

            if (naks.Ranges.Count > 0)
            {
                Console.WriteLine($"[ReliabilityLayer] UPDATE: Sending NAKs ({naks.Ranges.Count} ranges)");
                var nakPacket = new RakBitStream();
                nakPacket.Write((byte)0xA0); 
                SerializeRangeList(nakPacket, naks); 
                transmitAction(nakPacket.GetData(), nakPacket.GetNumberOfBytesUsed());
                naks.Ranges.Clear();
            }

            ProcessSendQueue(curTimeUS, timeSinceLastTick, transmitAction);
            ProcessResendQueue(curTimeUS, transmitAction);

            bool hasData = resendQueue.UnacknowledgedBytes > 0 || !sendQueue.IsEmpty;
            congestionManager.Update(curTimeUS, hasData);

            Statistics.Update(curTimeUS, resendQueue.UnacknowledgedBytes);
            Statistics.RTT = congestionManager.GetRTT();
        }
    }

    private void ProcessSendQueue(ulong curTimeUS, ulong timeSinceLastTick, Action<byte[], int> transmitAction)
    {
        if (sendQueue.IsEmpty) return;
        int bandwidthAllowed = congestionManager.GetTransmissionBandwidth(
            curTimeUS, 
            timeSinceLastTick, 
            resendQueue.UnacknowledgedBytes, 
            !sendQueue.IsEmpty
        );

        if (bandwidthAllowed <= 0) return;
        var packetsToSend = sendQueue.GetPacketsToAssemble(bandwidthAllowed);
        
        if (packetsToSend.Count == 0) return;

        Console.WriteLine($"[ReliabilityLayer] Sending {packetsToSend.Count} packets (bandwidth: {bandwidthAllowed} bytes)");

        foreach (var packet in packetsToSend)
        {
            byte packetIdForLog = packet.Data != null && packet.Data.Length > 0 ? packet.Data[0] : (byte)0xFF;
            Console.WriteLine($"[ProcessSendQueue] Processing packet 0x{packetIdForLog:X2}: Reliability={packet.Reliability} (value={(byte)packet.Reliability})");

            bool needsReliableMessageNumber = packet.Reliability == PacketReliability.RELIABLE ||
                                               packet.Reliability == PacketReliability.RELIABLE_ORDERED ||
                                               packet.Reliability == PacketReliability.RELIABLE_SEQUENCED ||
                                               packet.Reliability == PacketReliability.RELIABLE_WITH_ACK_RECEIPT ||
                                               packet.Reliability == PacketReliability.RELIABLE_ORDERED_WITH_ACK_RECEIPT;
            
            if (needsReliableMessageNumber)
            {
                packet.ReliableMessageNumber = GetNextReliableMessageNumber();
                Console.WriteLine($"[ProcessSendQueue] Assigned ReliableMessageNumber={packet.ReliableMessageNumber}");
            }
            else
            {
                Console.WriteLine($"[ProcessSendQueue] SKIPPING ReliableMessageNumber (unreliable type)");
            }
            
            if (packet.Reliability == PacketReliability.RELIABLE_ORDERED ||
                packet.Reliability == PacketReliability.UNRELIABLE_SEQUENCED ||
                packet.Reliability == PacketReliability.RELIABLE_SEQUENCED)
            {
                var channel = _orderingChannels[packet.OrderingChannel];
                packet.OrderingIndex = channel.GetNextSendOrderingIndex();
                Console.WriteLine($"[ProcessSendQueue] Assigned OrderingIndex={packet.OrderingIndex} on channel {packet.OrderingChannel}");
            }
            else
            {
                Console.WriteLine($"[ProcessSendQueue] SKIPPING OrderingIndex (unordered reliability)");
            }
            
            var datagram = new RakBitStream();
            
            byte headerByte = 0x80;
            bool isContinuousSend = !sendQueue.IsEmpty;
            if (isContinuousSend) headerByte |= 0x08;
            
            datagram.Write(headerByte);
            uint24 datagramNum = GetNextDatagramNumber();
            datagram.Write(datagramNum);
            WriteInternalPacket(datagram, packet);
            byte[] datagramData = datagram.GetData();
            int datagramSize = datagram.GetNumberOfBytesUsed();
            transmitAction(datagramData, datagramSize);
            congestionManager.OnDatagramSent(datagramNum, curTimeUS);

            Statistics.OnPacketSent(datagramSize);

            uint packetSize = (uint)(packet.Data?.Length ?? 0);
            congestionManager.OnSendBytes(curTimeUS, packetSize);

            bool isReliableType = packet.Reliability == PacketReliability.RELIABLE ||
                                  packet.Reliability == PacketReliability.RELIABLE_ORDERED ||
                                  packet.Reliability == PacketReliability.RELIABLE_SEQUENCED ||
                                  packet.Reliability == PacketReliability.RELIABLE_WITH_ACK_RECEIPT ||
                                  packet.Reliability == PacketReliability.RELIABLE_ORDERED_WITH_ACK_RECEIPT;

            if (isReliableType)
            {
                _datagramToMessage[datagramNum] = packet.ReliableMessageNumber;
                
                ulong rto = congestionManager.GetRTOForRetransmission();
                resendQueue.Add(packet, curTimeUS, rto);
            }
        }
    }

    private void ProcessResendQueue(ulong curTimeUS, Action<byte[], int> transmitAction)
    {
        var expiredPackets = resendQueue.GetExpired(curTimeUS).ToList();
        
        if (expiredPackets.Count == 0) return;

        Console.WriteLine($"[ReliabilityLayer] Resending {expiredPackets.Count} expired packets");

        foreach (var packet in expiredPackets)
        {
            var datagram = new RakBitStream();
            byte headerByte = 0x80;
            datagram.Write(headerByte);
            uint24 datagramNum = GetNextDatagramNumber();
            datagram.Write(datagramNum);
            WriteInternalPacket(datagram, packet);
            transmitAction(datagram.GetData(), datagram.GetNumberOfBytesUsed());
            Statistics.OnPacketResent();
            _datagramToMessage[datagramNum] = packet.ReliableMessageNumber;
            ulong rto = congestionManager.GetRTOForRetransmission();
            packet.NextActionTime = curTimeUS + rto;
            packet.TimesSent++;
            congestionManager.OnResend(curTimeUS);
        }
    }

    private void SendAcks(Action<byte[], int> transmitAction)
    {
        var ackPacket = new RakBitStream();
        
        // ACK Header (MSB): Valid(1)|Ack(1) -> 0xC0
        double asTemp;
        bool hasBAndAS;
        congestionManager.OnSendAckGetBAndAS(RakTime.GetTimeUS(), out hasBAndAS, out _, out asTemp);
        
        byte headerByte = 0xC0; 
        if (hasBAndAS) headerByte |= 0x20; 
        
        ackPacket.Write(headerByte);
        
        if (hasBAndAS)
        {
            ackPacket.Write((float)asTemp);
        }
        
        SerializeRangeList(ackPacket, acks);

        transmitAction(ackPacket.GetData(), ackPacket.GetNumberOfBytesUsed());
        
        acks.Ranges.Clear(); 
    }
    private void HandleAck(RakBitStream bs, ulong time) 
    {
        bs.AlignReadToByteBoundary();
        ushort count;
        if (!bs.Read(out count)) return;
        
        Console.WriteLine($"[HandleAck] Received ACK for {count} range(s)");
        
        for (uint i = 0; i < count; i++)
        {
            byte minEqualsMax;
            if (!bs.Read(out minEqualsMax)) return;
            uint24 minIndex;
            if (!bs.Read(out minIndex)) return;
            
            uint24 maxIndex;
            
            if (minEqualsMax == 0)
            {
                if (!bs.Read(out maxIndex)) return;
            }
            else
            {
                maxIndex = minIndex;
            }

            Console.WriteLine($"[HandleAck] Range: {minIndex} to {maxIndex}");

            for (uint24 datagramNum = minIndex; datagramNum <= maxIndex; datagramNum++)
            {
                if (_datagramToMessage.TryGetValue(datagramNum, out uint24 messageNum))
                {
                    resendQueue.OnAck(messageNum, out InternalPacket? ackedPacket);
                    
                    if (ackedPacket != null)
                    {
                        Console.WriteLine($"[HandleAck] Datagram {datagramNum} → Message {messageNum} ACKed");
                        congestionManager.OnGotAck(time, datagramNum);
                    }
                    
                    _datagramToMessage.Remove(datagramNum);
                }
            }
        }
    }

    private void HandleNak(RakBitStream bs, ulong time) 
    {
        bs.AlignReadToByteBoundary();
        ushort count;
        if (!bs.Read(out count)) return;
        
        Console.WriteLine($"[HandleNak] Received NAK for {count} range(s)");
        
        for (uint i = 0; i < count; i++)
        {
            byte minEqualsMax;
            if (!bs.Read(out minEqualsMax)) return;
            uint24 minIndex;
            if (!bs.Read(out minIndex)) return;
            uint24 maxIndex;
            if (minEqualsMax == 0)
            {
                if (!bs.Read(out maxIndex)) return;
            }
            else
            {
                maxIndex = minIndex;
            }
            
            Console.WriteLine($"[HandleNak] Range: {minIndex} to {maxIndex}");
            
            for (uint24 datagramNum = minIndex; datagramNum <= maxIndex; datagramNum++)
            {
                if (_datagramToMessage.TryGetValue(datagramNum, out uint24 messageNum))
                {
                    resendQueue.OnNak(messageNum, time);
                    
                    Console.WriteLine($"[HandleNak] Datagram {datagramNum} → Message {messageNum} marked for immediate resend");
                    
                    congestionManager.OnNAK(time, datagramNum);
                }
            }
        }
    }
    
    public void ProcessDatagram(RakBitStream bs, ulong time, Action<InternalPacket> onMessageReceived)
    {
        bs.AlignReadToByteBoundary();
        if (bs.GetNumberOfUnreadBits() < 8) return;

        byte headerByte;
        bs.Read(out headerByte);

        bool isValid = (headerByte & 0x80) != 0;
        if (!isValid) return;

        bool isAck = (headerByte & 0x40) != 0;
        bool isNak = (headerByte & 0x20) != 0;
        bool isContinuousSend = (headerByte & 0x08) != 0;

        Statistics.OnPacketReceived(bs.GetNumberOfBytesUsed());

        if (isAck) 
        { 
            bool hasBAndAS = (headerByte & 0x20) != 0; 
            if (hasBAndAS) bs.IgnoreBytes(4); 
            lock (_syncLock) HandleAck(bs, time); 
            return; 
        }
        
        if (isNak) 
        { 
            lock (_syncLock) HandleNak(bs, time); 
            return; 
        }

        ulong timeMS = time / 1000;
        OnDatagramArrived(timeMS);

        uint24 datagramNumber;
        bs.Read(out datagramNumber);
        
        Console.WriteLine($"[ReliabilityLayer] Datagram Number: {datagramNumber}");
        Console.WriteLine($"[ReliabilityLayer] Base Index: {_receivedPacketsBaseIndex}");

        lock (_syncLock) 
        {
            if (IsDuplicate(datagramNumber))
            {
                Console.WriteLine($"[ReliabilityLayer] Duplicate datagram #{datagramNumber}");
                acks.Insert(datagramNumber);
                return;
            }
            
            Console.WriteLine($"[ReliabilityLayer] NEW datagram #{datagramNumber} - Processing...");
            MarkDatagramReceived(datagramNumber);
            acks.Insert(datagramNumber);

            uint skipped;
            congestionManager.OnGotPacket(datagramNumber, isContinuousSend, time, (uint)bs.GetNumberOfBytesUsed(), out skipped);
            if (skipped > 0)
            {
                Console.WriteLine($"[ReliabilityLayer] Detected {skipped} skipped datagrams, adding to NAKs");
                uint24 start = datagramNumber - (uint24)skipped;
                for (uint24 i = start; i < datagramNumber; i++) naks.Insert(i);
            }
        }

        while (bs.GetNumberOfUnreadBits() > 0)
        {
            var packet = ReadInternalPacket(bs, time);
            if (packet == null) break;

            lock (_syncLock)
            {
                if (packet.SplitPacketCount > 0) HandleSplitPacket(packet, time, onMessageReceived);
                else HandleOrdering(packet, onMessageReceived);
            }
        }
    }

    private bool IsDuplicate(uint24 datagramNumber)
    {
        const uint MAX_RANGE = 0xFFFFFF;
        const uint HALF_RANGE = MAX_RANGE / 2;

        uint diff = datagramNumber.Value - _receivedPacketsBaseIndex.Value;
        
        Console.WriteLine($"[IsDuplicate] Checking: datagram={datagramNumber}, base={_receivedPacketsBaseIndex}, diff={diff}");
        
        if (diff == 0) 
        {
            Console.WriteLine($"[IsDuplicate] diff==0 -> NOT duplicate (expected packet)");
            return false;
        }

        if (diff > HALF_RANGE) 
        {
            Console.WriteLine($"[IsDuplicate] diff > HALF_RANGE -> IS duplicate (old packet)");
            return true;
        }
        
        if (diff < DATAGRAM_HISTORY_SIZE)
        {
            bool alreadyReceived = _hasReceivedPacketQueue[diff];
            Console.WriteLine($"[IsDuplicate] diff < HISTORY_SIZE -> history[{diff}] = {alreadyReceived}");
            return alreadyReceived;
        }

        Console.WriteLine($"[IsDuplicate] diff too large -> NOT duplicate (gap)");
        return false;
    }

    private void MarkDatagramReceived(uint24 datagramNumber)
    {
        const uint MAX_RANGE = 0xFFFFFF;
        const uint HALF_RANGE = MAX_RANGE / 2;

        uint diff = datagramNumber.Value - _receivedPacketsBaseIndex.Value;

        if (diff == 0)
        {
            _receivedPacketsBaseIndex++;
            if (_hasReceivedPacketQueue.Length > 0)
            {
                Array.Copy(_hasReceivedPacketQueue, 1, _hasReceivedPacketQueue, 0, _hasReceivedPacketQueue.Length - 1);
                _hasReceivedPacketQueue[_hasReceivedPacketQueue.Length - 1] = false;
                while (_hasReceivedPacketQueue[0])
                {
                    _receivedPacketsBaseIndex++;
                    Array.Copy(_hasReceivedPacketQueue, 1, _hasReceivedPacketQueue, 0, _hasReceivedPacketQueue.Length - 1);
                    _hasReceivedPacketQueue[_hasReceivedPacketQueue.Length - 1] = false;
                }
            }
        }
        else if (diff < DATAGRAM_HISTORY_SIZE)
        {
            _hasReceivedPacketQueue[diff] = true;
        }
        else if (diff > HALF_RANGE)
        {
            // Old packet, ignore
        }
        else
        {
            _receivedPacketsBaseIndex = datagramNumber + 1;
            Array.Clear(_hasReceivedPacketQueue, 0, _hasReceivedPacketQueue.Length);
        }
    }

    private void HandleOrdering(InternalPacket packet, Action<InternalPacket> onMessageReceived)
    {
        Console.WriteLine($"[HandleOrdering] Reliability: {packet.Reliability}");
        
        if (packet.Reliability == PacketReliability.RELIABLE_ORDERED || 
            packet.Reliability == PacketReliability.RELIABLE_ORDERED_WITH_ACK_RECEIPT)
        {
            Console.WriteLine($"[HandleOrdering] Ordered packet - channel {packet.OrderingChannel}");
            var channel = _orderingChannels[packet.OrderingChannel];
            channel.Push(packet);
            
            var inOrder = channel.PopInOrder().ToList();
            Console.WriteLine($"[HandleOrdering] Delivering {inOrder.Count} ordered packets");
            
            foreach (var p in inOrder) onMessageReceived(p);
        }
        else
        {
            Console.WriteLine($"[HandleOrdering] Unordered packet - delivering immediately");
            onMessageReceived(packet);
        }
    }

    private void HandleSplitPacket(InternalPacket packet, ulong time, Action<InternalPacket> onMessageReceived)
    {
        if (!_splitChannels.TryGetValue(packet.SplitPacketId, out var channel))
        {
            channel = new SplitPacketChannel { ReturnedPacket = packet };
            _splitChannels[packet.SplitPacketId] = channel;
        }

        if (!channel.SplitPackets.ContainsKey(packet.SplitPacketIndex))
        {
            channel.SplitPackets[packet.SplitPacketIndex] = packet;
            channel.SplitPacketsArrived++;
            channel.LastUpdateTime = time;
        }

        if (channel.IsComplete)
        {
            channel.SortFragments();
            var fullData = Reassemble(channel);
            var reassembledPacket = channel.ReturnedPacket!;
            reassembledPacket.Data = fullData;
            reassembledPacket.DataBitLength = fullData.Length * 8;
            reassembledPacket.SplitPacketCount = 0;

            _splitChannels.Remove(packet.SplitPacketId);
            HandleOrdering(reassembledPacket, onMessageReceived);
        }
    }

    private byte[] Reassemble(SplitPacketChannel channel)
    {
        int totalSize = 0;
        foreach (var p in channel.SplitPackets.Values) totalSize += p.Data.Length;
        byte[] buffer = new byte[totalSize];
        int offset = 0;
        foreach (var p in channel.SplitPackets.Values) 
        {
            Buffer.BlockCopy(p.Data, 0, buffer, offset, p.Data.Length);
            offset += p.Data.Length;
        }
        return buffer;
    }
    
    private List<InternalPacket> SplitPacketIfNeeded(InternalPacket packet, int mtu)
    {
        int headerSize = GetInternalPacketHeaderSize(packet);
        int maxDataSize = mtu - headerSize - 50;
        
        if (packet.Data.Length <= maxDataSize)
        {
            return new List<InternalPacket> { packet };
        }
        
        ushort splitPacketId = GetNextSplitPacketId();
        int dataPerPacket = maxDataSize;
        uint splitPacketCount = (uint)((packet.Data.Length + dataPerPacket - 1) / dataPerPacket);
        
        Console.WriteLine($"[ReliabilityLayer] Splitting packet into {splitPacketCount} chunks");
        
        var splitPackets = new List<InternalPacket>();
        
        for (uint splitPacketIndex = 0; splitPacketIndex < splitPacketCount; splitPacketIndex++)
        {
            int offset = (int)(splitPacketIndex * dataPerPacket);
            int length = Math.Min(dataPerPacket, packet.Data.Length - offset);
            
            var splitPacket = new InternalPacket
            {
                Reliability = packet.Reliability,
                Priority = packet.Priority,
                OrderingChannel = packet.OrderingChannel,
                OrderingIndex = packet.OrderingIndex,
                SplitPacketId = splitPacketId,
                SplitPacketIndex = splitPacketIndex,
                SplitPacketCount = splitPacketCount,
                Data = new byte[length],
                DataBitLength = length * 8,
                CreationTime = packet.CreationTime
            };
            
            Array.Copy(packet.Data, offset, splitPacket.Data, 0, length);
            
            Console.WriteLine($"[ReliabilityLayer] Split chunk {splitPacketIndex}/{splitPacketCount}: {length} bytes");
            
            splitPackets.Add(splitPacket);
        }
        
        return splitPackets;
    }

    private int GetInternalPacketHeaderSize(InternalPacket packet)
    {
        int size = 3;
        bool isReliableTypeRead = packet.Reliability == PacketReliability.RELIABLE ||
                              packet.Reliability == PacketReliability.RELIABLE_ORDERED ||
                              packet.Reliability == PacketReliability.RELIABLE_SEQUENCED ||
                              packet.Reliability == PacketReliability.RELIABLE_WITH_ACK_RECEIPT ||
                              packet.Reliability == PacketReliability.RELIABLE_ORDERED_WITH_ACK_RECEIPT;

        if (isReliableTypeRead)
            size += 3;
        
        if (packet.Reliability == PacketReliability.UNRELIABLE_SEQUENCED ||
            packet.Reliability == PacketReliability.RELIABLE_SEQUENCED ||
            packet.Reliability == PacketReliability.RELIABLE_ORDERED)
            size += 4;
        
        if (packet.SplitPacketCount > 0)
            size += 10;
        
        return size;
    }
    
    
    public void WriteInternalPacket(RakBitStream bs, InternalPacket internalPacket)
    {
        bs.AlignWriteToByteBoundary();
        
        byte header = (byte)((byte)internalPacket.Reliability << 5);
        if (internalPacket.SplitPacketCount > 0) header |= 0x10; 
        bs.Write(header);

        ushort bits = (ushort)internalPacket.DataBitLength;
        bs.Write((byte)(bits >> 8));
        bs.Write((byte)(bits & 0xFF));

        bool isReliableType = internalPacket.Reliability == PacketReliability.RELIABLE ||
                              internalPacket.Reliability == PacketReliability.RELIABLE_ORDERED ||
                              internalPacket.Reliability == PacketReliability.RELIABLE_SEQUENCED ||
                              internalPacket.Reliability == PacketReliability.RELIABLE_WITH_ACK_RECEIPT ||
                              internalPacket.Reliability == PacketReliability.RELIABLE_ORDERED_WITH_ACK_RECEIPT;

        if (isReliableType)
        {
            bs.Write(internalPacket.ReliableMessageNumber);
        }

        if (internalPacket.Reliability == PacketReliability.UNRELIABLE_SEQUENCED ||
            internalPacket.Reliability == PacketReliability.RELIABLE_SEQUENCED ||
            internalPacket.Reliability == PacketReliability.RELIABLE_ORDERED)
        {
            bs.Write(internalPacket.OrderingIndex);
            bs.Write(internalPacket.OrderingChannel);
        }

        if (internalPacket.SplitPacketCount > 0)
        {
            bs.Write(internalPacket.SplitPacketCount);   // uint
            bs.Write(internalPacket.SplitPacketId);      // ushort
            bs.Write(internalPacket.SplitPacketIndex);   // uint
        }

        bs.AlignWriteToByteBoundary();
        bs.WriteBits(internalPacket.Data, internalPacket.DataBitLength, true);
    }

    public InternalPacket? ReadInternalPacket(RakBitStream bs, ulong time)
    {
        bs.AlignReadToByteBoundary();
        if (bs.GetNumberOfUnreadBits() < 24) return null;

        InternalPacket packet = new InternalPacket { CreationTime = time };

        byte headerByte;
        bs.Read(out headerByte);

        packet.Reliability = (PacketReliability)((headerByte >> 5) & 0x07);
        bool hasSplit = ((headerByte >> 4) & 0x01) != 0;
        packet.SplitPacketCount = hasSplit ? 1u : 0u;

        byte l1, l2;
        bs.Read(out l1);
        bs.Read(out l2);
        packet.DataBitLength = (ushort)((l1 << 8) | l2);

        bool isReliableTypeForRead = packet.Reliability == PacketReliability.RELIABLE ||
                              packet.Reliability == PacketReliability.RELIABLE_ORDERED ||
                              packet.Reliability == PacketReliability.RELIABLE_SEQUENCED ||
                              packet.Reliability == PacketReliability.RELIABLE_WITH_ACK_RECEIPT ||
                              packet.Reliability == PacketReliability.RELIABLE_ORDERED_WITH_ACK_RECEIPT;

        if (isReliableTypeForRead)
        {
            bs.Read(out packet.ReliableMessageNumber);
        }

        if (packet.Reliability == PacketReliability.UNRELIABLE_SEQUENCED ||
            packet.Reliability == PacketReliability.RELIABLE_SEQUENCED ||
            packet.Reliability == PacketReliability.RELIABLE_ORDERED)
        {
            bs.Read(out packet.OrderingIndex);
            bs.Read(out packet.OrderingChannel);
        }

        if (hasSplit)
        {
            bs.Read(out packet.SplitPacketCount);   // uint
            bs.Read(out packet.SplitPacketId);      // ushort
            bs.Read(out packet.SplitPacketIndex);   // uint
        }

        bs.AlignReadToByteBoundary();
        int bytesToRead = (packet.DataBitLength + 7) >> 3;
        
        if (bytesToRead <= 0 || bs.GetNumberOfUnreadBits() < packet.DataBitLength) return null;

        packet.Data = new byte[bytesToRead];
        for(int i=0; i<bytesToRead; i++) bs.Read(out packet.Data[i]);

        return packet;
    }
    
    private uint24 ReadUInt24BE(RakBitStream bs)
    {
        byte b1, b2, b3;
        bs.Read(out b1);
        bs.Read(out b2);
        bs.Read(out b3);
        return new uint24((uint)((b1 << 16) | (b2 << 8) | b3));
    }

    private void WriteUInt24BE(RakBitStream bs, uint24 val)
    {
        uint v = val;
        bs.Write((byte)((v >> 16) & 0xFF));
        bs.Write((byte)((v >> 8) & 0xFF));
        bs.Write((byte)(v & 0xFF));
    }

    private uint ReadUInt32BE(RakBitStream bs)
    {
        byte b1, b2, b3, b4;
        bs.Read(out b1); bs.Read(out b2); bs.Read(out b3); bs.Read(out b4);
        return (uint)((b1 << 24) | (b2 << 16) | (b3 << 8) | b4);
    }

    private void WriteUInt32BE(RakBitStream bs, uint val)
    {
        bs.Write((byte)((val >> 24) & 0xFF));
        bs.Write((byte)((val >> 16) & 0xFF));
        bs.Write((byte)((val >> 8) & 0xFF));
        bs.Write((byte)(val & 0xFF));
    }

    private ushort ReadUInt16BE(RakBitStream bs)
    {
        byte b1, b2;
        bs.Read(out b1); bs.Read(out b2);
        return (ushort)((b1 << 8) | b2);
    }

    private void WriteUInt16BE(RakBitStream bs, ushort val)
    {
        bs.Write((byte)((val >> 8) & 0xFF));
        bs.Write((byte)(val & 0xFF));
    }

    private void SerializeRangeList(RakBitStream bs, RangeList<uint24> list)
    {
        bs.AlignWriteToByteBoundary();
        bs.Write((ushort)list.Ranges.Count);

        foreach (var range in list.Ranges)
        {
            byte minEqualsMax = (byte)(range.MinIndex == range.MaxIndex ? 1 : 0);
            bs.Write(minEqualsMax);
            bs.Write(range.MinIndex);
            
            if (minEqualsMax == 0)
            {
                bs.Write(range.MaxIndex);
            }
        }
    }
}