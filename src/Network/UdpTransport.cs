using System.Net;
using System.Net.Sockets;

namespace RakNexus.Network;

public class UdpTransport : IDisposable
{
    private readonly UdpClient _socket;
    private readonly int _port;
    private bool _disposed;

    public UdpTransport(int port)
    {
        _port = port;
        _socket = new UdpClient(port);
        _socket.Client.ReceiveBufferSize = 1024 * 1024;
        _socket.Client.SendBufferSize = 1024 * 1024;
        
        RakLog.Trace($"[UdpTransport] Socket bound to port {port}");
    }

    public async Task<(int len, EndPoint remote)> ReceiveAsync(byte[] buffer)
    {
        try
        {
            var result = await _socket.ReceiveAsync();
            
            if (result.Buffer.Length > buffer.Length)
            {
                RakLog.Trace($"[UdpTransport] Received packet too large! " +
                    $"Got {result.Buffer.Length} bytes, buffer is {buffer.Length} bytes");
                return (0, result.RemoteEndPoint);
            }
            
            Array.Copy(result.Buffer, buffer, result.Buffer.Length);
            return (result.Buffer.Length, result.RemoteEndPoint);
        }
        catch (SocketException ex)
        {
            RakLog.Error($"[UdpTransport] ReceiveAsync error: {ex.Message}");
            return (0, new IPEndPoint(IPAddress.Any, 0));
        }
    }

    public void Send(byte[] data, int length, EndPoint remote)
    {
        if (_disposed)
        {
            RakLog.Trace("[UdpTransport] Attempted to send on disposed socket");
            return;
        }
        try
        {
            var ipEndPoint = (IPEndPoint)remote;
            byte[] sendBuffer = new byte[length];
            Array.Copy(data, sendBuffer, length);
            
            _socket.Send(sendBuffer, length, ipEndPoint);
        }
        catch (SocketException ex)
        {
            RakLog.Error($"[UdpTransport] Send error to {remote}: {ex.Message}");
        }
    }

    public void Send(ReadOnlySpan<byte> data, EndPoint remote)
    {
        if (_disposed)
        {
            RakLog.Trace("[UdpTransport] Attempted to send on disposed socket");
            return;
        }
        try
        {
            var ipEndPoint = (IPEndPoint)remote;
            byte[] sendBuffer = data.ToArray();
            _socket.Send(sendBuffer, sendBuffer.Length, ipEndPoint);
        }
        catch (SocketException ex)
        {
            RakLog.Error($"[UdpTransport] Send error to {remote}: {ex.Message}");
        }
    }

    public void Close()
    {
        if (!_disposed)
        {
            _socket.Close();
            _disposed = true;
            RakLog.Trace($"[UdpTransport] Closed socket on port {_port}");
        }
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    ~UdpTransport()
    {
        Dispose();
    }
}

public class UdpTransportRaw : IDisposable
{
    private readonly Socket _socket;
    private readonly int _port;
    private bool _disposed;

    public UdpTransportRaw(int port)
    {
        _port = port;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.ReceiveBufferSize = 1024 * 1024;
        _socket.SendBufferSize = 1024 * 1024;
        
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        
        RakLog.Trace($"[UdpTransportRaw] Socket bound to port {port}");
    }

    public async Task<(int len, EndPoint remote)> ReceiveAsync(byte[] buffer)
    {
        try
        {
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            var result = await _socket.ReceiveFromAsync(
                new ArraySegment<byte>(buffer), 
                SocketFlags.None, 
                remoteEndPoint
            );
            
            return (result.ReceivedBytes, result.RemoteEndPoint);
        }
        catch (SocketException ex)
        {
            RakLog.Error($"[UdpTransportRaw] ReceiveAsync error: {ex.Message}");
            return (0, new IPEndPoint(IPAddress.Any, 0));
        }
    }

    public void Send(byte[] data, int length, EndPoint remote)
    {
        if (_disposed) return;

        try
        {
            _socket.SendTo(data, 0, length, SocketFlags.None, remote);
        }
        catch (SocketException ex)
        {
            RakLog.Error($"[UdpTransportRaw] Send error to {remote}: {ex.Message}");
        }
    }

    public void Send(ReadOnlySpan<byte> data, EndPoint remote)
    {
        if (_disposed) return;

        try
        {
            _socket.SendTo(data, SocketFlags.None, remote);
        }
        catch (SocketException ex)
        {
            RakLog.Error($"[UdpTransportRaw] Send error to {remote}: {ex.Message}");
        }
    }

    public void Close()
    {
        if (!_disposed)
        {
            _socket.Close();
            _disposed = true;
            RakLog.Trace($"[UdpTransportRaw] Closed socket on port {_port}");
        }
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    ~UdpTransportRaw()
    {
        Dispose();
    }
}
