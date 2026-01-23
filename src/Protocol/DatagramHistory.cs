using RakNexus.Core;

namespace RakNexus.Protocol;

public class HistoryNode
{
    public readonly List<uint24> MessageNumbers = new();
}

public class DatagramHistory
{
    private const int HISTORY_SIZE = 512;
    private readonly Queue<HistoryNode?> _historyQueue = new();
    private uint24 _historyStart = 0;

    public void AddFirst(uint24 datagramNumber, uint24 messageNumber)
    {
        while (_historyQueue.Count > HISTORY_SIZE)
        {
            _historyQueue.Dequeue();
            _historyStart++;
        }

        var node = new HistoryNode();
        node.MessageNumbers.Add(messageNumber);
        _historyQueue.Enqueue(node);
    }

    public void AddSubsequent(uint24 messageNumber)
    {
        var node = _historyQueue.Last();
        node?.MessageNumbers.Add(messageNumber);
    }

    public HistoryNode? GetAndClear(uint24 datagramNumber)
    {
        if (datagramNumber < _historyStart) return null;
        int offset = (int)(datagramNumber.Value - _historyStart.Value);
        
        if (offset >= _historyQueue.Count) return null;
        
        var node = _historyQueue.ElementAt(offset);
        return node;
    }
}