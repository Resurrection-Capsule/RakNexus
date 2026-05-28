using RakNexus.Core;
using DatagramSequenceNumberType = RakNexus.Core.uint24;

namespace RakNexus.Protocol;

public class CongestionManager
{
    private const double UNSET_TIME_US = -1.0;
    private const double CWND_MIN_THRESHOLD = 2.0;
    private const double UNDEFINED_TRANSFER_RATE = 0.0;
    private const ulong SYN = 10000; // 10ms
    private const double MAX_RTT = 1000000.0;
    private const double RTT_TOLERANCE = 30000.0;
    private const int RAKNET_UDT_PACKET_HISTORY_LENGTH = 64;

    private double SND;
    private double CWND;
    private double RTT;
    private double minRTT, maxRTT;
    private double AS;
    
    private bool isInSlowStart;
    private uint24 nextDatagramSequenceNumber;
    private ulong nextSYNUpdate;
    
    private ulong lastPacketArrivalTime;
    private ulong lastTransmitOfBAndAS;
    private ulong oldestUnsentAck;
    private uint MAXIMUM_MTU_INCLUDING_UDP_HEADER;
    private double CWND_MAX_THRESHOLD = 65535; 

    private double[] packetArrivalHistory = new double[RAKNET_UDT_PACKET_HISTORY_LENGTH];
    private int packetArrivalHistoryWriteIndex;
    private uint packetArrivalHistoryWriteCount;
    
    private int bytesCanSendThisTick;
    private ulong totalUserDataBytesSent;
    private ulong lastRttOnIncreaseSendRate;
    
    private DatagramSequenceNumberType expectedNextSequenceNumber;
    private readonly Dictionary<uint24, ulong> _datagramSendTimes = new();
    private const int MAX_TRACKED_DATAGRAMS = 512;

    public CongestionManager()
    {
        for(int i=0; i<packetArrivalHistory.Length; i++) packetArrivalHistory[i] = UNDEFINED_TRANSFER_RATE;
    }

    public void Init(ulong curTime, uint maxDatagramPayload)
    {
        nextSYNUpdate = 0;
        packetArrivalHistoryWriteIndex = 0;
        packetArrivalHistoryWriteCount = 0;
        RTT = UNSET_TIME_US;
        isInSlowStart = true;
        nextDatagramSequenceNumber = 0;
        lastPacketArrivalTime = 0;
        CWND = CWND_MIN_THRESHOLD;
        AS = UNDEFINED_TRANSFER_RATE;
        MAXIMUM_MTU_INCLUDING_UDP_HEADER = maxDatagramPayload;
        minRTT = 0;
        maxRTT = 0;
        SND = 1.0 / 0.0036; 
        
        lastRttOnIncreaseSendRate = 1000000; 
        bytesCanSendThisTick = 0;
        
        for(int i=0; i<packetArrivalHistory.Length; i++) packetArrivalHistory[i] = UNDEFINED_TRANSFER_RATE;
    }

    public void Update(ulong curTime, bool hasDataToSend)
    {
        // TODO
    }

    // --- Sequence ---
    public uint24 GetAndIncrementNextDatagramSequenceNumber() => nextDatagramSequenceNumber++;
    public uint24 GetNextDatagramSequenceNumber() => nextDatagramSequenceNumber;

    // --- Bandwidth ---
    
    public int GetTransmissionBandwidth(ulong curTime, ulong timeSinceLastTick, uint unacknowledgedBytes, bool isContinuousSend)
    {
        if (isInSlowStart)
        {
            double limit = CWND * MAXIMUM_MTU_INCLUDING_UDP_HEADER - unacknowledgedBytes;
            return (int)limit;
        }
        
        if (bytesCanSendThisTick > 0) bytesCanSendThisTick = 0;
        if (!isContinuousSend && timeSinceLastTick > 100000) timeSinceLastTick = 100000;

        bytesCanSendThisTick = (int)((double)timeSinceLastTick * (1.0 / SND) + (double)bytesCanSendThisTick);
        
        return Math.Max(0, bytesCanSendThisTick);
    }
    
    public int GetRetransmissionBandwidth(ulong curTime, ulong timeSinceLastTick, uint unacknowledgedBytes, bool isContinuousSend)
    {
        if (isInSlowStart)
        {
            return (int)(CWND * MAXIMUM_MTU_INCLUDING_UDP_HEADER);
        }
        return GetTransmissionBandwidth(curTime, timeSinceLastTick, unacknowledgedBytes, isContinuousSend);
    }

    // --- Events ---

    public void OnSendBytes(ulong curTime, uint numBytes)
    {
        totalUserDataBytesSent += numBytes;
        if (!isInSlowStart) bytesCanSendThisTick -= (int)numBytes;
    }

    public bool OnGotPacket(uint24 datagramNumber, bool isContinuousSend, ulong curTime, uint length, out uint skippedMessageCount)
    {
        if (datagramNumber == expectedNextSequenceNumber)
        {
            skippedMessageCount = 0;
            expectedNextSequenceNumber = datagramNumber + 1;
        }
        else if (datagramNumber > expectedNextSequenceNumber)
        {
            skippedMessageCount = (uint)(datagramNumber - expectedNextSequenceNumber).Value;
            if(skippedMessageCount > 1000) skippedMessageCount = 1000;
            
            expectedNextSequenceNumber = datagramNumber + 1;
        }
        else
        {
            skippedMessageCount = 0;
        }

        if (curTime > lastPacketArrivalTime)
        {
            ulong interval = curTime - lastPacketArrivalTime;
            if (isContinuousSend)
            {
                packetArrivalHistory[packetArrivalHistoryWriteIndex++] = (double)length / (double)interval;
                packetArrivalHistoryWriteIndex &= (RAKNET_UDT_PACKET_HISTORY_LENGTH - 1);
                packetArrivalHistoryWriteCount++;
            }
            lastPacketArrivalTime = curTime;
        }
        
        return true;
    }

    public void OnGotPacketPair(uint24 datagramNumber, uint length, ulong curTime)
    {
    }

    public void OnResend(ulong curTime)
    {
        if (isInSlowStart)
        {
            if (AS != UNDEFINED_TRANSFER_RATE) EndSlowStart();
            return;
        }
        SND *= 1.1; 
    }

    public void OnNAK(ulong curTime, uint24 nakSequenceNumber)
    {
        if (isInSlowStart && AS != UNDEFINED_TRANSFER_RATE) EndSlowStart();
    }
    public void OnDatagramSent(uint24 datagramNumber, ulong sendTime)
    {
        if (_datagramSendTimes.Count >= MAX_TRACKED_DATAGRAMS)
        {
            var oldest = _datagramSendTimes.Keys.Min();
            _datagramSendTimes.Remove(oldest);
        }
        
        _datagramSendTimes[datagramNumber] = sendTime;
    }

    public void OnGotAck(ulong curTime, uint24 datagramNumber)
    {
        if (_datagramSendTimes.TryGetValue(datagramNumber, out ulong sendTime))
        {
            ulong rtt = curTime - sendTime;
            
            RakLog.Trace($"[CongestionManager] RTT for datagram {datagramNumber}: {rtt / 1000.0:F2}ms");

            if (RTT == UNSET_TIME_US)
            {
                RTT = rtt;
                minRTT = rtt;
                maxRTT = rtt;
            }
            else
            {
                RTT = (RTT * 0.875) + (rtt * 0.125);
                
                if (rtt < minRTT) minRTT = rtt;
                if (rtt > maxRTT) maxRTT = rtt;
            }
            
            _datagramSendTimes.Remove(datagramNumber);
        }
    }

    public void OnAck(ulong curTime, ulong rtt, bool hasBAndAS, double B, double _AS, double totalBytesAcked, bool isContinuousSend, uint24 sequenceNumber)
    {
        if (rtt > 10000000) rtt = 10000;

        if (oldestUnsentAck == 0) oldestUnsentAck = curTime;

        if (hasBAndAS)
        {
            AS = _AS; 
        }

        if (isInSlowStart)
        {
            CWND = totalBytesAcked / MAXIMUM_MTU_INCLUDING_UDP_HEADER;
            if (CWND >= CWND_MAX_THRESHOLD)
            {
                CWND = CWND_MAX_THRESHOLD;
                if (AS != UNDEFINED_TRANSFER_RATE) EndSlowStart();
            }
            if (CWND < CWND_MIN_THRESHOLD) CWND = CWND_MIN_THRESHOLD;
        }
        else
        {
            UpdateWindowSizeAndAckOnAckPerSyn(curTime, rtt, isContinuousSend, sequenceNumber);
        }
    }

    private void EndSlowStart()
    {
        isInSlowStart = false;
        SND = 1.0 / AS; 
        if (SND > 500) SND = 500;
    }

    // --- Control ---

    public bool ShouldSendAcks(ulong curTime, ulong timeSinceLastTick)
    {
        return curTime >= oldestUnsentAck + SYN;
    }

    public void ResetOldestAck() => oldestUnsentAck = 0;

    public void OnSendAck(ulong curTime, uint numBytes) { oldestUnsentAck = 0; }

    public void OnSendAckGetBAndAS(ulong curTime, out bool hasBAndAS, out double outB, out double outAS)
    {
        if (curTime > lastTransmitOfBAndAS + SYN)
        {
            outB = 0; 
            outAS = CalculateDataArrivalRateMedian();
            hasBAndAS = (outAS != UNDEFINED_TRANSFER_RATE);
            lastTransmitOfBAndAS = curTime;
        }
        else
        {
            hasBAndAS = false; outB = 0; outAS = 0;
        }
    }

    private double CalculateDataArrivalRateMedian()
    {
        if (packetArrivalHistoryWriteCount < RAKNET_UDT_PACKET_HISTORY_LENGTH) return UNDEFINED_TRANSFER_RATE;
        
        double sum = 0;
        for(int i=0; i<RAKNET_UDT_PACKET_HISTORY_LENGTH; i++) sum += packetArrivalHistory[i];
        return sum / RAKNET_UDT_PACKET_HISTORY_LENGTH;
    }

    // --- RTO ---

    public ulong GetSenderRTOForACK()
    {
        if (RTT == UNSET_TIME_US) return 0;
        return (ulong)(RTT + 4.0 * (maxRTT - minRTT) + SYN);
    }

    public ulong GetRTOForRetransmission()
    {
        if (RTT == UNSET_TIME_US) return 1000000;
        
        ulong ret = (ulong)(lastRttOnIncreaseSendRate * 2.0);
        return Math.Clamp(ret, 100000, 1000000); 
    }

    // --- Helpers ---
    public bool LessThan(uint24 a, uint24 b) => a < b;
    public ulong GetMTU() => MAXIMUM_MTU_INCLUDING_UDP_HEADER;
    public double GetRTT() => RTT == UNSET_TIME_US ? 0.0 : RTT;
    public bool GetIsInSlowStart() => isInSlowStart;
    
    public ulong GetBytesPerSecondLimitByCongestionControl() 
    { 
        if (isInSlowStart) return 0;
        return (ulong)(1.0 / (SND * 1000000.0));
    }
    
    public void OnExternalPing(double pingMS)
    {
        RTT = pingMS * 1000.0;
        if (RTT == 0) RTT = UNSET_TIME_US;
        
        minRTT = RTT;
        maxRTT = RTT;
        
        lastRttOnIncreaseSendRate = (ulong)RTT;
    }

    private void UpdateWindowSizeAndAckOnAckPerSyn(ulong curTime, ulong rtt, bool isContinuousSend, uint24 seq)
    {
        if (!isContinuousSend) return;
        RTT = rtt; 
    }
}
