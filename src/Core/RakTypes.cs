using System.Net;
using System.Net.Sockets;

namespace RakNexus.Core;

public struct SystemAddress : IEquatable<SystemAddress>
{
    public uint BinaryAddress;
    public ushort Port;

    public static readonly SystemAddress Unassigned = new SystemAddress(0xFFFFFFFF, 0xFFFF);

    public SystemAddress(uint address, ushort port)
    {
        BinaryAddress = address;
        Port = port;
    }

    public SystemAddress(IPEndPoint endpoint)
    {
        byte[] addressBytes = endpoint.Address.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
        {
            BinaryAddress = (uint)(addressBytes[3] | (addressBytes[2] << 8) | (addressBytes[1] << 16) | (addressBytes[0] << 24));
        }
        else
        {
            BinaryAddress = BitConverter.ToUInt32(addressBytes, 0);
        }
        Port = (ushort)endpoint.Port;
    }

    public System.Net.IPEndPoint ToIPEndPoint()
    {
        if (this == Unassigned) return new IPEndPoint(IPAddress.Any, 0);
        return new IPEndPoint(new IPAddress(BitConverter.GetBytes(BinaryAddress)), Port);
    }

    public override string ToString()
    {
        if (this == Unassigned) return "UNASSIGNED_SYSTEM_ADDRESS";
        byte b0 = (byte)((BinaryAddress >> 24) & 0xFF);
        byte b1 = (byte)((BinaryAddress >> 16) & 0xFF);
        byte b2 = (byte)((BinaryAddress >> 8) & 0xFF);
        byte b3 = (byte)(BinaryAddress & 0xFF);
        return $"{b0}.{b1}.{b2}.{b3}:{Port}";
    }

    public bool Equals(SystemAddress other) => BinaryAddress == other.BinaryAddress && Port == other.Port;
    public override bool Equals(object? obj) => obj is SystemAddress other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(BinaryAddress, Port);

    public static bool operator ==(SystemAddress left, SystemAddress right) => left.Equals(right);
    public static bool operator !=(SystemAddress left, SystemAddress right) => !left.Equals(right);
}

public struct RakNetGUID : IEquatable<RakNetGUID>
{
    public ulong G;
    public static readonly RakNetGUID Unassigned = new RakNetGUID(ulong.MaxValue);

    public RakNetGUID(ulong guid) => G = guid;

    public bool Equals(RakNetGUID other) => G == other.G;
    public override bool Equals(object? obj) => obj is RakNetGUID other && Equals(other);
    public override int GetHashCode() => G.GetHashCode();
    public override string ToString() => G.ToString();

    public static bool operator ==(RakNetGUID left, RakNetGUID right) => left.Equals(right);
    public static bool operator !=(RakNetGUID left, RakNetGUID right) => !left.Equals(right);
}

public struct NetworkId : IEquatable<NetworkId>
{
    public RakNetGUID Guid;
    public ushort LocalSystemAddress;

    public static readonly NetworkId Unassigned = new NetworkId { LocalSystemAddress = 65535, Guid = RakNetGUID.Unassigned };

    public bool Equals(NetworkId other) => Guid == other.Guid && LocalSystemAddress == other.LocalSystemAddress;
    public override bool Equals(object? obj) => obj is NetworkId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Guid, LocalSystemAddress);

    public static bool operator ==(NetworkId left, NetworkId right) => left.Equals(right);
    public static bool operator !=(NetworkId left, NetworkId right) => !left.Equals(right);
}