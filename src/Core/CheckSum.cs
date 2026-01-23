namespace RakNexus.Core;

public class CheckSum
{
    private ushort r;
    private ushort c1;
    private ushort c2;
    private uint sum;

    public CheckSum()
    {
        Clear();
    }

    public void Clear()
    {
        sum = 0;
        r = 55665;
        c1 = 52845;
        c2 = 22719;
    }

    public uint Get() => sum;

    public void Add(uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        for (int i = 0; i < bytes.Length; i++) Add(bytes[i]);
    }

    public void Add(ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        for (int i = 0; i < bytes.Length; i++) Add(bytes[i]);
    }

    public void Add(byte[] b, int length)
    {
        for (int i = 0; i < length; i++) Add(b[i]);
    }

    public void Add(byte value)
    {
        byte cipher = (byte)(value ^ (r >> 8));
        r = (ushort)((cipher + r) * c1 + c2);
        sum += cipher;
    }
}