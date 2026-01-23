using System.Diagnostics;

namespace RakNexus.Core;

public static class RakTime
{
    private static readonly Stopwatch _sw = Stopwatch.StartNew();
    private static ulong _lastInputValue;
    private static ulong _lastNormalizedValue;
    private const ulong SPIKE_LIMIT = 1000000; // 1 ms

    public static ulong GetTimeUS()
    {
        return (ulong)(_sw.ElapsedTicks / (double)Stopwatch.Frequency * 1_000_000);
    }

    public static ulong GetTimeMS()
    {
        return (ulong)(_sw.ElapsedMilliseconds);
    }

    public static uint GetTime()
    {
        return (uint)GetTimeMS();
    }
    
    private static ulong NormalizeTime(ulong timeIn)
    {
        lock (_sw)
        {
            if (timeIn >= _lastInputValue)
            {
                ulong diff = timeIn - _lastInputValue;
                if (diff > SPIKE_LIMIT)
                    _lastNormalizedValue += SPIKE_LIMIT;
                else
                    _lastNormalizedValue += diff;
            }
            else
            {
                _lastNormalizedValue += SPIKE_LIMIT;
            }

            _lastInputValue = timeIn;
            return _lastNormalizedValue;
        }
    }
}