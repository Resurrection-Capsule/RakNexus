using RakNexus.Core;
using RakNexus.Protocol;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace RakNexus.Network;

public class RakNetListener : IDisposable
{
    private UdpClient? _socket;
    private readonly int _port;
    private readonly ConcurrentDictionary<EndPoint, RakNetSession> _sessions = new();
    private readonly RakNetGUID _serverGuid;
    private CancellationTokenSource _cts = new();
    private bool _disposed = false;
    private bool _running = false;

    public bool IsRunning => _running && _socket != null;

    public event Action<RakNetSession>? SessionConnected;

    public RakNetListener(int port)
    {
        _port = port;
        
        byte[] guidBytes = new byte[8];
        Random.Shared.NextBytes(guidBytes);
        _serverGuid = new RakNetGUID(BitConverter.ToUInt64(guidBytes, 0));
        
        Console.WriteLine($"[RakNetListener] Server GUID: 0x{_serverGuid.G:X16}");
    }

    private void CreateSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(new IPEndPoint(IPAddress.Any, _port));
        _socket = new UdpClient();
        _socket.Client = socket;

        Console.WriteLine($"[RakNetListener] Socket created on port {_port} with SO_REUSEADDR");
    }

    public async Task StartAsync()
    {
        if (_running)
        {
            Console.WriteLine("[RakNetListener] Already running!");
            return;
        }
        
        try 
        {
            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }
            
            _sessions.Clear();
            CreateSocket();
            _running = true;
            _ = Task.Run(() => UpdateLoop(_cts.Token));

            Console.WriteLine("[RakNetListener] Started receiving loop...");
            while (!_cts.Token.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(_cts.Token);
                byte[] data = result.Buffer;
                EndPoint remote = result.RemoteEndPoint;
                if (data.Length == 0) continue;
                Console.WriteLine($"[RakNetListener] UDP: len={data.Length}, first=0x{data[0]:X2} ({(MessageId)data[0]}), from={remote}");
                if (data[0] == 0x09 || data[0] == (byte)MessageId.ID_OPEN_CONNECTION_REQUEST)
                {
                    Console.WriteLine($"[RakNetListener] DETECTED ID_OPEN_CONNECTION_REQUEST (0x{data[0]:X2})!");
                }
                
                Console.WriteLine($"[RakNetListener] HEXDUMP: {BitConverter.ToString(data, 0, Math.Min(data.Length, 32))}");
                
                if (IsQosProbe(data))
                {
                    ProcessQosProbe(data, remote);
                    continue;
                }
                
                Console.WriteLine("[RakNetListener] >>> NON-QOS PACKET RECEIVED <<<");

                if (IsOfflineMessage(data))
                {
                    ProcessOfflineMessage(data, remote);
                }
                else
                {
                    if (!_sessions.TryGetValue(remote, out var session))
                    {
                        Console.WriteLine($"[RakNetListener] Ignoring packet from unknown system {remote} (no offline handshake completed)");
                        Console.WriteLine($"[RakNetListener] First byte: 0x{data[0]:X2} is NOT a recognized offline message ID");
                        continue;
                    }
                    
                    session.HandleIncoming(data.AsSpan());
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[RakNetListener] Receive loop cancelled.");
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("[RakNetListener] Socket disposed, stopping receive loop.");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
        {
            Console.WriteLine("[RakNetListener] Socket interrupted, stopping receive loop.");
        }
        catch (Exception ex)
        {
            if (_running)
                Console.WriteLine($"[RakNetListener] FATAL: {ex}");
        }
        finally
        {
            _running = false;
            Console.WriteLine("[RakNetListener] Receive loop stopped.");
        }
    }

    public void Stop()
    {
        Console.WriteLine("[RakNetListener] Stopping...");
        _running = false;
        try { _cts.Cancel(); } catch { }
        try 
        { 
            _socket?.Close();
            _socket?.Dispose();
            _socket = null;
        } 
        catch (Exception ex) 
        { 
            Console.WriteLine($"[RakNetListener] Error closing socket: {ex.Message}"); 
        }
        foreach (var session in _sessions.Values)
        {
            try { session.ForceDisconnect(); } catch { }
        }
        _sessions.Clear();
        
        Console.WriteLine("[RakNetListener] Stopped.");
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        Stop();
        
        try { _cts.Dispose(); } catch { }
    }

    private async Task UpdateLoop(CancellationToken ct)
    {
        Console.WriteLine("[RakNetListener] Update loop started");
        
        while (!ct.IsCancellationRequested && _running)
        {
            try
            {
                ulong now = RakTime.GetTimeUS();
                foreach (var session in _sessions.Values.ToArray())
                {
                    if (!_running) break;
                    session.Update(now);
                }

                await Task.Delay(10, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_running)
                    Console.WriteLine($"[RakNetListener] Update error: {ex.Message}");
            }
        }
        
        Console.WriteLine("[RakNetListener] Update loop stopped");
    }

    private bool IsOfflineMessage(byte[] data)
    {
        if (data.Length < 2) return false;
        byte packetId = data[0];
        bool isKnownOfflineMessage = 
            packetId == (byte)MessageId.ID_OPEN_CONNECTION_REQUEST ||
            packetId == (byte)MessageId.ID_OPEN_CONNECTION_REPLY ||
            packetId == (byte)MessageId.ID_ADVERTISE_SYSTEM ||
            packetId == (byte)MessageId.ID_PING ||
            packetId == (byte)MessageId.ID_PONG ||
            packetId == (byte)MessageId.ID_PING_OPEN_CONNECTIONS ||
            packetId == (byte)MessageId.ID_INCOMPATIBLE_PROTOCOL_VERSION;
        
        if (!isKnownOfflineMessage)
            return false; 
        
        int magicOffset;
        
        if (packetId == (byte)MessageId.ID_OPEN_CONNECTION_REQUEST)
        {
            magicOffset = 10;
        }
        else
        {
            magicOffset = 1;
        }
        
        if (data.Length < magicOffset + RakConstants.OFFLINE_MESSAGE_DATA_ID.Length)
            return false;
        
        for (int i = 0; i < RakConstants.OFFLINE_MESSAGE_DATA_ID.Length; i++)
        {
            if (data[magicOffset + i] != RakConstants.OFFLINE_MESSAGE_DATA_ID[i])
                return false;
        }
        
        return true;
    }

    private void ProcessOfflineMessage(byte[] data, EndPoint remote)
    {
        byte packetId = data[0];
        
        if (packetId == (byte)MessageId.ID_OPEN_CONNECTION_REQUEST)
        {
            Console.WriteLine($"[RakNetListener] OPEN_CONNECTION_REQUEST from {remote}");
            int requestLength = data.Length;
            var ipEp = (IPEndPoint)remote;
            var clientAddress = new SystemAddress(ipEp);
            
            var response = new RakBitStream();
            response.Write((byte)MessageId.ID_OPEN_CONNECTION_REPLY);
            response.WriteAlignedBytes(RakConstants.OFFLINE_MESSAGE_DATA_ID);
            response.Write(_serverGuid);
            response.Write(clientAddress);
            
            int currentSize = response.GetNumberOfBytesUsed();
            if (requestLength > currentSize)
            {
                byte[] padding = new byte[requestLength - currentSize];
                response.WriteBits(padding, padding.Length * 8, true);
            }
            
            byte[] responseData = response.GetData();
            int responseLen = response.GetNumberOfBytesUsed();
            Console.WriteLine($"[RakNetListener] Sending OPEN_CONNECTION_REPLY: {responseLen} bytes to {clientAddress}");
            Console.WriteLine($"[RakNetListener] REPLY HEXDUMP: {BitConverter.ToString(responseData, 0, Math.Min(responseLen, 32))}");
            Send(responseData, responseLen, remote);
            
            if (!_sessions.TryGetValue(remote, out var session))
            {
                session = new RakNetSession(remote, this, _serverGuid.G);
                session.State = ConnectionState.Unverified;
                
                session.OnConnected += () => 
                {
                    Console.WriteLine($"[RakNetListener] Session Connected: {remote}");
                    SessionConnected?.Invoke(session);
                };
                
                session.Disconnected += (reason) =>
                {
                    Console.WriteLine($"[RakNetListener] Session Disconnected: {remote}");
                    _sessions.TryRemove(remote, out _);
                };
                
                _sessions.TryAdd(remote, session);
                Console.WriteLine($"[RakNetListener] Created session for {remote} in UNVERIFIED state");
            }
        }
    }

    public void Send(byte[] data, int length, EndPoint remote)
    {
        if (_socket == null || !_running)
        {
            Console.WriteLine($"[RakNetListener.Send] Socket not available, skipping send");
            return;
        }
        
        try
        {
            Console.WriteLine($"[RakNetListener.Send] Sending {length} bytes to {remote}, First byte: 0x{data[0]:X2}");
            int sent = _socket.Send(data, length, (IPEndPoint)remote);
            Console.WriteLine($"[RakNetListener.Send] Sent {sent} bytes successfully");
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine($"[RakNetListener] Socket disposed, skipping send");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RakNetListener] Send Error: {ex.Message}");
            Console.WriteLine($"[RakNetListener] Stack: {ex.StackTrace}");
        }
    }
    
    private bool IsQosProbe(byte[] data)
    {
        if (data.Length < 8) return false;
        
        // Read version (bytes 4-7, big-endian)
        uint version = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);
        
        // V1 probes are exactly 20 bytes, V2 probes are exactly 8 bytes
        if (version == 1 && data.Length == 20) return true;
        if (version == 2 && data.Length == 8) return true;
        
        return false;
    }

    private void ProcessQosProbe(byte[] data, EndPoint remote)
    {
        var ipEp = (IPEndPoint)remote;
        uint id = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
        uint version = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);
        
        byte[] response;
        int offset = 0;

        void WriteU32BE(byte[] buf, uint val)
        {
            buf[offset++] = (byte)((val >> 24) & 0xFF);
            buf[offset++] = (byte)((val >> 16) & 0xFF);
            buf[offset++] = (byte)((val >> 8) & 0xFF);
            buf[offset++] = (byte)(val & 0xFF);
        }
        
        void WriteU16BE(byte[] buf, ushort val)
        {
            buf[offset++] = (byte)((val >> 8) & 0xFF);
            buf[offset++] = (byte)(val & 0xFF);
        }
        
        if (version == 1)
        {
            uint requestSecret = (uint)((data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11]);
            uint requestId = (uint)((data[12] << 24) | (data[13] << 16) | (data[14] << 8) | data[15]);
            uint ticks = (uint)((data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19]);
            
            Console.WriteLine($"[RakNetListener] QoS V1 Probe: id={id}, secret=0x{requestSecret:X}, reqId={requestId}, ticks={ticks}");
            
            response = new byte[64];
            
            WriteU32BE(response, id);
            WriteU32BE(response, version);
            WriteU32BE(response, requestSecret);
            WriteU32BE(response, requestId);
            WriteU32BE(response, ticks);
            
            byte[] ipBytes = ipEp.Address.GetAddressBytes();
            if (ipBytes.Length == 4)
            {
                response[offset++] = ipBytes[0];
                response[offset++] = ipBytes[1];
                response[offset++] = ipBytes[2];
                response[offset++] = ipBytes[3];
            }
            else
            {
                offset += 4;
            }
            
            WriteU16BE(response, 3659);
            WriteU32BE(response, 0x12345678);
        }
        else if (version == 2)
        {
            Console.WriteLine($"[RakNetListener] QoS V2 Probe: id={id}, version={version}");
            
            // V2 Response: 44 bytes (11 x uint32)
            // Based on C++ server.cpp:
            // mWriteBuffer.write_u32_be(id);
            // mWriteBuffer.write_u32_be(version);
            // mWriteBuffer.write_u32_be(0x1337);    // request secret
            // mWriteBuffer.write_u32_be(2);          // probe id
            // mWriteBuffer.write_u32_be(0);
            // mWriteBuffer.write_u32_be(2);          // offset: 0x120
            // mWriteBuffer.write_u32_be(0xDEAD);
            // mWriteBuffer.write_u32_be(2);
            // mWriteBuffer.write_u32_be(0xBEEF);
            // mWriteBuffer.write_u32_be(2);
            // mWriteBuffer.write_u32_be(0x8925);
            
            response = new byte[44];
            
            WriteU32BE(response, id);
            WriteU32BE(response, version);
            WriteU32BE(response, 0x1337);    // request secret
            WriteU32BE(response, 2);          // probe id
            WriteU32BE(response, 0);
            WriteU32BE(response, 2);          // offset: 0x120
            WriteU32BE(response, 0xDEAD);
            WriteU32BE(response, 2);
            WriteU32BE(response, 0xBEEF);
            WriteU32BE(response, 2);
            WriteU32BE(response, 0x8925);
        }
        else
        {
            Console.WriteLine($"[RakNetListener] Unknown QoS version: {version}");
            return;
        }
        
        Console.WriteLine($"[RakNetListener] QoS V{version} Response: {response.Length} bytes to {ipEp}");
        Console.WriteLine($"[RakNetListener] QoS Response HEXDUMP: {BitConverter.ToString(response, 0, Math.Min(response.Length, 44))}");
        
        Send(response, response.Length, remote);
    }
}