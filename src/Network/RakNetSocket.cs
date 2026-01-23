using System.Net;
using System.Net.Sockets;
using RakNexus.Core;

namespace RakNexus.Network;

public class RakNetSocket : IDisposable
{
    public Socket RawSocket { get; }
    public SystemAddress BoundAddress { get; }
    public uint UserConnectionSocketIndex { get; set; }

    public RakNetSocket(int port, string? host = null)
    {
        RawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        ConfigureSocket();

        IPAddress addr = string.IsNullOrEmpty(host) ? IPAddress.Any : IPAddress.Parse(host);
        var ep = new IPEndPoint(addr, port);
        RawSocket.Bind(ep);
        
        var localEp = (IPEndPoint)RawSocket.LocalEndPoint!;
        BoundAddress = new SystemAddress(localEp);
    }

    private void ConfigureSocket()
    {
        RawSocket.ReceiveBufferSize = 1024 * 256;
        RawSocket.SendBufferSize = 1024 * 16;
        RawSocket.Blocking = false;
        RawSocket.EnableBroadcast = true;
        
        try
        {
            RawSocket.DontFragment = true;
        }
        catch (SocketException) { /* Handle legacy OS if necessary */ }
    }

    public void Dispose()
    {
        RawSocket.Dispose();
    }
}