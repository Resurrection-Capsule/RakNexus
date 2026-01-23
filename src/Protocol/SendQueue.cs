namespace RakNexus.Protocol;

public class SendQueue
{
    private readonly Queue<InternalPacket>[] _priorityQueues;
    private readonly ulong[] _nextWeights = new ulong[(int)PacketPriority.NUMBER_OF_PRIORITIES];
    private ulong _seriesWeight;

    public SendQueue()
    {
        _priorityQueues = new Queue<InternalPacket>[(int)PacketPriority.NUMBER_OF_PRIORITIES];
        for (int i = 0; i < _priorityQueues.Length; i++)
        {
            _priorityQueues[i] = new Queue<InternalPacket>();
            _nextWeights[i] = (ulong)((1 << i) * i + i);
        }
    }

    public void Enqueue(InternalPacket packet)
    {
        _priorityQueues[(int)packet.Priority].Enqueue(packet);
    }

    public ulong GetNextWeight(PacketPriority priority)
    {
        int p = (int)priority;
        ulong weight = _nextWeights[p];
        _nextWeights[p] = weight + (ulong)((1 << p) * (p + 1) + p);
        return weight;
    }

    public List<InternalPacket> GetPacketsToAssemble(int maxBytes)
    {
        var result = new List<InternalPacket>();
        int currentBytes = 0;

        while (currentBytes < maxBytes)
        {
            InternalPacket? bestPacket = null;
            int bestPriority = -1;

            for (int i = 0; i < (int)PacketPriority.NUMBER_OF_PRIORITIES; i++)
            {
                if (_priorityQueues[i].Count > 0)
                {
                    bestPacket = _priorityQueues[i].Peek();
                    bestPriority = i;
                    break; 
                }
            }

            if (bestPacket == null || (currentBytes + (bestPacket.HeaderLength / 8) + bestPacket.Data.Length) > maxBytes)
                break;

            _priorityQueues[bestPriority].Dequeue();
            result.Add(bestPacket);
            currentBytes += (bestPacket.HeaderLength / 8) + bestPacket.Data.Length;
        }

        return result;
    }

    public bool IsEmpty => _priorityQueues.All(q => q.Count == 0);
}