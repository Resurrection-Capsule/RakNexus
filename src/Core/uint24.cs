namespace RakNexus.Core;

public struct uint24 : IComparable<uint24>, IEquatable<uint24>
{
    private uint _value;
    public uint Value 
    { 
        get => _value; 
        set => _value = value & 0x00FFFFFF; 
    }

    public uint24(uint value) => _value = value & 0x00FFFFFF;

    public static implicit operator uint(uint24 val) => val._value;
    public static implicit operator uint24(uint val) => new uint24(val);

    public static uint24 operator ++(uint24 a) => new uint24(a._value + 1);
    public static uint24 operator --(uint24 a) => new uint24(a._value - 1);
    
    public static uint24 operator +(uint24 a, uint24 b) => new uint24(a._value + b._value);
    public static uint24 operator -(uint24 a, uint24 b) => new uint24(a._value - b._value);
    
    public static uint24 operator +(uint24 a, int b) => new uint24((uint)(a._value + b));
    public static uint24 operator -(uint24 a, int b) => new uint24((uint)(a._value - b));

    public bool Equals(uint24 other) => _value == other._value;
    public int CompareTo(uint24 other) => _value.CompareTo(other._value);
    
    public override bool Equals(object? obj) => obj is uint24 other && Equals(other);
    public override int GetHashCode() => (int)_value;
    public override string ToString() => _value.ToString();

    public static bool operator ==(uint24 left, uint24 right) => left.Equals(right);
    public static bool operator !=(uint24 left, uint24 right) => !left.Equals(right);
    public static bool operator <(uint24 left, uint24 right) => left._value < right._value;
    public static bool operator >(uint24 left, uint24 right) => left._value > right._value;
    public static bool operator <=(uint24 left, uint24 right) => left._value <= right._value;
    public static bool operator >=(uint24 left, uint24 right) => left._value >= right._value;
}