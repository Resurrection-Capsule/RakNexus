using RakNexus.Core;

namespace RakNexus.Network;

public class PeerState
{
    public ushort MaxIncomingConnections { get; set; }
    public ushort MaxNumberOfPeers { get; set; }
    public RakNetGUID Guid { get; }
    private readonly HashSet<string> _securityExceptionList = new();
    private readonly object _syncLock = new();

    public PeerState(ushort maxPeers)
    {
        MaxNumberOfPeers = maxPeers;
        byte[] guidBytes = new byte[8];
        Random.Shared.NextBytes(guidBytes);
        Guid = new RakNetGUID(BitConverter.ToUInt64(guidBytes, 0));
    }

    public bool AllowIncomingConnection(int currentConnectedCount)
    {
        return currentConnectedCount < MaxIncomingConnections;
    }

    public void AddSecurityException(string ip)
    {
        lock (_syncLock) _securityExceptionList.Add(ip);
    }

    public bool IsInSecurityExceptionList(string ip)
    {
        lock (_syncLock) return _securityExceptionList.Contains(ip);
    }
}