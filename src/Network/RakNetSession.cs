using RakNexus.Protocol;
using RakNexus.Core;
using System.Net;

namespace RakNexus.Network;

public enum ConnectionState
{
    Unverified,
    Connecting,
    Connected,
    Disconnecting,
    Disconnected
}

public class RakNetSession
{
    public readonly SystemAddress Address;
    public RakNetGUID Guid { get; private set; }
    public ConnectionState State { get; set; } = ConnectionState.Unverified;
    private readonly RakNetListener _listener;
    private readonly EndPoint _remoteEndPoint;
    private readonly ReliabilityLayer _reliability;
    private readonly ulong _serverGuid;
    private ulong _lastReliableSendTime;
    private const int TIMEOUT_MS = 10000;
    public event Action<Packet>? PacketReceived;
    public event Action<string>? Disconnected;
    public event Action? OnConnected;
    public event Action? OnNewIncomingConnection;

    public RakNetSession(EndPoint remote, RakNetListener listener, ulong serverGuid)
    {
        var ipEp = (IPEndPoint)remote;
        Address = new SystemAddress(ipEp);
        _remoteEndPoint = remote;
        _listener = listener;
        _serverGuid = serverGuid;
        
        Guid = RakNetGUID.Unassigned;

        _reliability = new ReliabilityLayer();
        _reliability.SetTimeoutTime(TIMEOUT_MS);
        
        _lastReliableSendTime = RakTime.GetTimeMS();
        
        Console.WriteLine($"[RakNetSession] Created for {Address}");
    }

    public void SetGuid(RakNetGUID guid)
    {
        Guid = guid;
        Console.WriteLine($"[RakNetSession] GUID set to 0x{guid.G:X16}");
    }
    

    public void QueueInternalPacket(InternalPacket packet)
    {
        _reliability.sendQueue.Enqueue(packet);
        
        if (packet.Reliability >= PacketReliability.RELIABLE)
        {
            _lastReliableSendTime = RakTime.GetTimeMS();
        }
        
        ulong now = RakTime.GetTimeUS();
        _reliability.Update(now, (data, len) => {
            _listener.Send(data, len, _remoteEndPoint);
        });
        
        Console.WriteLine($"[RakNetSession] Queued and flushed internal packet: 0x{packet.Data[0]:X2}");
    }

    public void Update(ulong nowUS)
    {
        ulong nowMS = nowUS / 1000;

        if (State == ConnectionState.Connected)
        {
            if (nowMS - _lastReliableSendTime > (_reliability.TimeoutTime / 2))
            {
                SendPing(nowMS);
                _lastReliableSendTime = nowMS;
            }
        }

        if (_reliability.IsDeadConnection(nowMS))
        {
            CloseConnection("Timeout");
            return;
        }

        _reliability.Update(nowUS, (data, len) => {
            _listener.Send(data, len, _remoteEndPoint);
        });
    }

    public void HandleIncoming(ReadOnlySpan<byte> data)
    {
        if (State == ConnectionState.Disconnected) return;

        ulong now = RakTime.GetTimeUS();
        var bs = new RakBitStream(data.ToArray(), data.Length, false);
        
        Console.WriteLine($"[RakNetSession] HandleIncoming: {data.Length} bytes (State: {State})");
        if (data.Length > 0)
        {
            Console.WriteLine($"[RakNetSession] First byte: 0x{data[0]:X2}");
        }
        
        _reliability.ProcessDatagram(bs, now, OnInternalPacketReceived);
    }

    private void OnInternalPacketReceived(InternalPacket packet)
    {
        if (packet.Data == null || packet.Data.Length == 0) return;

        byte packetId = packet.Data[0];
        
        Console.WriteLine($"[RakNetSession] Received internal packet: 0x{packetId:X2} ({(MessageId)packetId})");

        switch ((MessageId)packetId)
        {
            case MessageId.ID_CONNECTION_REQUEST:
                HandleConnectionRequest(packet);
                return;
                
            case MessageId.ID_NEW_INCOMING_CONNECTION:
                HandleNewIncomingConnection(packet);
                return;
                
            case MessageId.ID_DISCONNECTION_NOTIFICATION:
            case MessageId.ID_CONNECTION_LOST:
                CloseConnection("Client Disconnected");
                return;
                
            case MessageId.ID_INTERNAL_PING:
                HandlePing(packet);
                return;
                
            case MessageId.ID_CONNECTED_PONG:
                return;
        }

        if (State == ConnectionState.Connected)
        {
            var p = new Packet(packet.Data, packet.Data.Length, Address, Guid);
            PacketReceived?.Invoke(p);
        }
    }

    private void HandleConnectionRequest(InternalPacket packet)
    {
        if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
        {
            Console.WriteLine("[RakNetSession] Ignoring duplicate CONNECTION_REQUEST (already processing/connected)");
            return;
        }
        
        Console.WriteLine($"[RakNetSession] Processing CONNECTION_REQUEST (current state: {State})");
        
        var bs = new RakBitStream(packet.Data, packet.Data.Length, false);
        bs.IgnoreBytes(1);
        bs.IgnoreBytes(RakConstants.OFFLINE_MESSAGE_DATA_ID.Length);
        
        RakNetGUID clientGuid;
        bs.Read(out clientGuid);
        
        ulong timestamp;
        bs.Read(out timestamp);

        Guid = clientGuid;
        
        Console.WriteLine($"[RakNetSession] Client GUID: 0x{clientGuid.G:X16}");
        Console.WriteLine($"[RakNetSession] Timestamp: {timestamp}");
        
        byte o0 = (byte)((Address.BinaryAddress >> 24) & 0xFF);
        byte o1 = (byte)((Address.BinaryAddress >> 16) & 0xFF);
        byte o2 = (byte)((Address.BinaryAddress >> 8) & 0xFF);
        byte o3 = (byte)(Address.BinaryAddress & 0xFF);
        Console.WriteLine($"[RakNetSession] Writing client address: {o0}.{o1}.{o2}.{o3}:{Address.Port}");
        Console.WriteLine($"[RakNetSession] XORed bytes will be: [{(byte)~o0}, {(byte)~o1}, {(byte)~o2}, {(byte)~o3}]");

        var response = new RakBitStream();
        response.Write((byte)MessageId.ID_CONNECTION_REQUEST_ACCEPTED);
        response.Write(Address);
        byte[] indexBytes = BitConverter.GetBytes((ushort)0);
        response.WriteBits(indexBytes, 16, true);
        
        for (int i = 0; i < RakConstants.MAXIMUM_NUMBER_OF_INTERNAL_IDS; i++)
        {
            response.Write(SystemAddress.Unassigned); 
        }
        
        // RakNetTime is 8 bytes (__GET_TIME_64BIT=1), big-endian on the wire.
        response.Write(timestamp);
        response.Write(RakTime.GetTimeMS());

        int actualSize = response.GetNumberOfBytesUsed();
        var responseData = response.GetData();
        Console.WriteLine($"[RakNetSession] CONN_REQ_ACCEPTED size: {actualSize} bytes (expected: 85)");
        Console.WriteLine($"[RakNetSession] CONN_REQ_ACCEPTED bytes: {BitConverter.ToString(responseData, 0, Math.Min(20, actualSize))}");

        SendInternal(response, PacketPriority.IMMEDIATE_PRIORITY, 
            PacketReliability.RELIABLE_ORDERED, 0);

        State = ConnectionState.Connecting;
        
        Console.WriteLine($"[RakNetSession] Connection accepted, sent CONN_REQ_ACCEPTED");
        Console.WriteLine($"[RakNetSession] State -> Connecting (waiting for NEW_INCOMING_CONNECTION)");
    }

    private void HandleNewIncomingConnection(InternalPacket packet)
    {
        Console.WriteLine($"[RakNetSession] Received NEW_INCOMING_CONNECTION, data length: {packet.Data.Length}");
        Console.WriteLine($"[RakNetSession] Raw bytes: {BitConverter.ToString(packet.Data, 0, Math.Min(20, packet.Data.Length))}");
        
        if (State != ConnectionState.Connecting)
        {
            Console.WriteLine($"[RakNetSession] Ignoring NEW_INCOMING_CONNECTION in state {State}");
            return;
        }
        
        var bs = new RakBitStream(packet.Data, packet.Data.Length, false);
        bs.IgnoreBytes(1); // MessageID
        
        SystemAddress serverAddr;
        bs.Read(out serverAddr);
        
        for (int i = 0; i < RakConstants.MAXIMUM_NUMBER_OF_INTERNAL_IDS; i++)
        {
            SystemAddress internalAddr;
            if (!bs.Read(out internalAddr)) break;
        }
        
        ulong sendPingTime, sendPongTime;
        bs.Read(out sendPingTime);
        bs.Read(out sendPongTime);
        
        byte o0 = (byte)((serverAddr.BinaryAddress >> 24) & 0xFF);
        byte o1 = (byte)((serverAddr.BinaryAddress >> 16) & 0xFF);
        byte o2 = (byte)((serverAddr.BinaryAddress >> 8) & 0xFF);
        byte o3 = (byte)(serverAddr.BinaryAddress & 0xFF);
        Console.WriteLine($"[RakNetSession] Client sees server as: {o0}.{o1}.{o2}.{o3}:{serverAddr.Port}");
        Console.WriteLine($"[RakNetSession] Ping times: sent={sendPingTime}, pong={sendPongTime}");
        
        State = ConnectionState.Connected;
        Console.WriteLine($"[RakNetSession] RakNet Handshake COMPLETE - State -> Connected");
        
        OnConnected?.Invoke();
        OnNewIncomingConnection?.Invoke();
    }

    private void HandlePing(InternalPacket packet)
    {
        var bs = new RakBitStream(packet.Data, packet.Data.Length, false);
        bs.IgnoreBytes(1);
        ulong clientTime;
        bs.Read(out clientTime);

        Console.WriteLine($"[RakNetSession] Handling PING with clientTime={clientTime}");

        var response = new RakBitStream();
        response.Write((byte)MessageId.ID_CONNECTED_PONG);
        response.Write(clientTime);
        response.Write(RakTime.GetTimeMS());
        
        SendInternal(response, PacketPriority.IMMEDIATE_PRIORITY, 
            PacketReliability.UNRELIABLE, 0);
        
        Console.WriteLine($"[RakNetSession] Forcing immediate PONG flush...");
        ulong now = RakTime.GetTimeUS();
        _reliability.Update(now, (data, len) => {
            _listener.Send(data, len, _remoteEndPoint);
        });
        Console.WriteLine($"[RakNetSession] PONG flush complete");
    }

    private void CloseConnection(string reason)
    {
        if (State == ConnectionState.Disconnected) return;
        
        Console.WriteLine($"[RakNetSession] Closing: {reason}");
        State = ConnectionState.Disconnected;
        Disconnected?.Invoke(reason);
    }
    
    public void ForceDisconnect()
    {
        State = ConnectionState.Disconnected;
        Console.WriteLine($"[RakNetSession] Force disconnected: {Address}");
    }

    private void SendPing(ulong nowMS)
    {
        var bs = new RakBitStream();
        bs.Write((byte)MessageId.ID_INTERNAL_PING);
        bs.Write(nowMS);
        SendInternal(bs, PacketPriority.IMMEDIATE_PRIORITY, 
            PacketReliability.RELIABLE, 0);
    }
    
    private void SendInternal(RakBitStream bs, PacketPriority priority, 
        PacketReliability reliability, byte orderingChannel)
    {
        var internalPacket = new InternalPacket
        {
            Data = bs.GetData(),
            DataBitLength = bs.GetNumberOfBitsUsed(),
            Priority = priority,
            Reliability = reliability,
            OrderingChannel = orderingChannel,
            OrderingIndex = 0,
            CreationTime = RakTime.GetTimeUS(),
            ReliableMessageNumber = 0
        };
        
        _reliability.sendQueue.Enqueue(internalPacket);
        
        if (reliability >= PacketReliability.RELIABLE)
        {
            _lastReliableSendTime = RakTime.GetTimeMS();
        }
        ulong now = RakTime.GetTimeUS();
        _reliability.Update(now, (data, len) => {
            _listener.Send(data, len, _remoteEndPoint);
        });
        
        Console.WriteLine($"[RakNetSession.SendInternal] Flushed packet immediately");
    }

    public void Send(byte[] data, PacketPriority priority, PacketReliability reliability, byte channel, byte orderingChannel)
    {
        var actualReliability = PacketReliability.UNRELIABLE_WITH_ACK_RECEIPT;
        const byte FIXED_CHANNEL = 0;
        
        Console.WriteLine($"[RakNetSession.Send] Packet 0x{data[0]:X2}, size={data.Length}");
        Console.WriteLine($"[RakNetSession.Send] Requested: reliability={reliability}, channel={orderingChannel}");
        Console.WriteLine($"[RakNetSession.Send] FORCING: reliability={actualReliability}, channel={FIXED_CHANNEL}");
        
        try
        {
            var bs = new RakBitStream(data, data.Length, true);
            
            var internalPacket = new InternalPacket
            {
                Data = bs.GetData(),
                DataBitLength = bs.GetNumberOfBitsUsed(),
                Priority = priority,
                Reliability = actualReliability,
                OrderingChannel = FIXED_CHANNEL,
                OrderingIndex = 0,
                CreationTime = RakTime.GetTimeUS(),
                ReliableMessageNumber = 0
            };
            
            _reliability.sendQueue.Enqueue(internalPacket);
            
            if (actualReliability >= PacketReliability.RELIABLE)
            {
                _lastReliableSendTime = RakTime.GetTimeMS();
            }
            
            Console.WriteLine($"[RakNetSession.Send] Packet enqueued successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RakNetSession.Send] ERROR: {ex.Message}");
            throw;
        }
    }
}
