namespace RakNexus.Core;

public class RakRandom
{
    private const int N = 624;
    private const int M = 397;
    private const uint K = 0x9908B0DFU;

    private readonly uint[] _state = new uint[N + 1];
    private int _left = -1;
    private int _nextIdx = 0;

    public RakRandom(uint seed) => Seed(seed);

    public void Seed(uint seed)
    {
        _state[0] = seed & 0xffffffffU;
        for (int j = 1; j < N; j++)
        {
            _state[j] = (1812433253U * (_state[j - 1] ^ (_state[j - 1] >> 30)) + (uint)j);
            _state[j] &= 0xffffffffU;
        }
        _left = 1; _nextIdx = 0;
    }
    
    private uint Reload()
    {
        int p0 = 0, pM = M, p2 = 2;
        uint s0, s1;

        _left = N - 1;
        _nextIdx = 1;

        s0 = _state[p0];
        s1 = _state[p0 + 1];

        for (int j = N - M + 1; --j > 0; )
        {
             s0 = _state[p0];
             s1 = _state[p0 + 1];
             _state[p0] = _state[pM++] ^ (MixBits(s0, s1) >> 1) ^ ((s1 & 1) != 0 ? K : 0U);
             
             s0 = s1;
             s1 = _state[p2++];
             p0++;
        }
        return 0;
    }

    public uint Next()
    {
        if (--_left < 0) return Reload();
        uint y = _state[_nextIdx++];
        y ^= (y >> 11);
        y ^= (y << 7) & 0x9D2C5680U;
        y ^= (y << 15) & 0xEFC60000U;
        return y ^ (y >> 18);
    }

    private uint MixBits(uint u, uint v) => (u & 0x80000000U) | (v & 0x7FFFFFFFU);
    // public uint Next() => (uint)Random.Shared.Next();
}