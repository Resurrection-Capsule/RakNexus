namespace RakNexus.Protocol;

public class RakNetStatistics
{
    public ulong MessagesSent;
    public ulong MessagesReceived;
    public ulong BytesSent;
    public ulong BytesReceived;
    public ulong PacketsLost;
    public ulong PacketsResent;
    public ulong MessagesInResendBuffer;
    
    private ulong _lastUpdateTime;
    private ulong _bytesSentLastSecond;
    private ulong _bytesReceivedLastSecond;
    
    public double BitsPerSecondSent { get; private set; }
    public double BitsPerSecondReceived { get; private set; }
    
    public double RTT { get; set; }
    public double PacketLossPercentage => 
        MessagesSent > 0 ? (PacketsLost * 100.0 / MessagesSent) : 0.0;
    
    public void OnPacketSent(int bytes)
    {
        MessagesSent++;
        BytesSent += (ulong)bytes;
    }
    
    public void OnPacketReceived(int bytes)
    {
        MessagesReceived++;
        BytesReceived += (ulong)bytes;
    }
    
    public void OnPacketLost()
    {
        PacketsLost++;
    }
    
    public void OnPacketResent()
    {
        PacketsResent++;
    }
    
    public void Update(ulong curTimeUS, uint messagesInBuffer)
    {
        MessagesInResendBuffer = messagesInBuffer;
        
        if (_lastUpdateTime == 0)
        {
            _lastUpdateTime = curTimeUS;
            return;
        }
        
        ulong elapsed = curTimeUS - _lastUpdateTime;
        if (elapsed >= 1000000) // 1s
        {
            ulong bytesSentThisSecond = BytesSent - _bytesSentLastSecond;
            ulong bytesReceivedThisSecond = BytesReceived - _bytesReceivedLastSecond;
            
            BitsPerSecondSent = (bytesSentThisSecond * 8.0 * 1000000.0) / elapsed;
            BitsPerSecondReceived = (bytesReceivedThisSecond * 8.0 * 1000000.0) / elapsed;
            
            _bytesSentLastSecond = BytesSent;
            _bytesReceivedLastSecond = BytesReceived;
            _lastUpdateTime = curTimeUS;
        }
    }
    
    public void PrintStatistics()
    {
        Console.WriteLine(ToString());
    }
    
    public override string ToString()
    {
        return $@"
=== RakNet Statistics ===
Messages Sent: {MessagesSent:N0}
Messages Received: {MessagesReceived:N0}
Bytes Sent: {BytesSent:N0}
Bytes Received: {BytesReceived:N0}
Packets Lost: {PacketsLost}
Packets Resent: {PacketsResent}
In Resend Buffer: {MessagesInResendBuffer}
BPS Sent: {BitsPerSecondSent / 1000.0:F2} Kbps
BPS Received: {BitsPerSecondReceived / 1000.0:F2} Kbps
RTT: {RTT / 1000.0:F2} ms
Packet Loss: {PacketLossPercentage:F2}%
";
    }
}