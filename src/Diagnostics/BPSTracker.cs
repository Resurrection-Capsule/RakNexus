namespace RakNexus.Diagnostics;

public class BPSTracker
{
    private struct TimeAndValue
    {
        public ulong Time;
        public ulong Value;
        public TimeAndValue(ulong t, ulong v) { Time = t; Value = v; }
    }

    private readonly Queue<TimeAndValue> _dataQueue = new();
    private ulong _total;
    private ulong _lastSec;

    public void Reset()
    {
        _total = 0;
        _lastSec = 0;
        _dataQueue.Clear();
    }

    public void Push(ulong time, ulong value)
    {
        ClearExpired(time);
        _dataQueue.Enqueue(new TimeAndValue(time, value));
        _total += value;
        _lastSec += value;
    }

    public ulong GetBPS(ulong time)
    {
        ClearExpired(time);
        return _lastSec;
    }

    public ulong GetTotal() => _total;

    private void ClearExpired(ulong time)
    {
        while (_dataQueue.Count > 0 && _dataQueue.Peek().Time + 1000000 < time)
        {
            _lastSec -= _dataQueue.Dequeue().Value;
        }
    }
}