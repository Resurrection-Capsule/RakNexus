using RakNexus.Network;
using RakNexus.Protocol;

namespace RakNexus.Core;

public struct AddressOrGUID : IEquatable<AddressOrGUID>
{
    public SystemAddress SystemAddress;
    public RakNetGUID RakNetGuid;

    public AddressOrGUID(SystemAddress systemAddress)
    {
        SystemAddress = systemAddress;
        RakNetGuid = RakNetGUID.Unassigned;
    }

    public AddressOrGUID(RakNetGUID guid)
    {
        SystemAddress = SystemAddress.Unassigned;
        RakNetGuid = guid;
    }

    public AddressOrGUID(InternalPacket packet)
    {
        SystemAddress = SystemAddress.Unassigned; 
        RakNetGuid = RakNetGUID.Unassigned;
    }

    public bool IsUndefined() => SystemAddress == SystemAddress.Unassigned && RakNetGuid == RakNetGUID.Unassigned;

    public static implicit operator AddressOrGUID(SystemAddress sa) => new AddressOrGUID(sa);
    public static implicit operator AddressOrGUID(RakNetGUID guid) => new AddressOrGUID(guid);

    public bool Equals(AddressOrGUID other) => SystemAddress == other.SystemAddress && RakNetGuid == other.RakNetGuid;
    public override bool Equals(object? obj) => obj is AddressOrGUID other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(SystemAddress, RakNetGuid);
    
    public static bool operator ==(AddressOrGUID left, AddressOrGUID right) => left.Equals(right);
    public static bool operator !=(AddressOrGUID left, AddressOrGUID right) => !left.Equals(right);
}