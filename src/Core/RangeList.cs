namespace RakNexus.Core;

public struct RangeNode<T> where T : struct, IComparable<T>
{
    public T MinIndex;
    public T MaxIndex;

    public RangeNode(T min, T max)
    {
        MinIndex = min;
        MaxIndex = max;
    }
}

public class RangeList<T> where T : struct, IComparable<T>
{
    public List<RangeNode<T>> Ranges = new();

    public void Insert(T index)
    {
        if (Ranges.Count == 0)
        {
            Ranges.Add(new RangeNode<T>(index, index));
            return;
        }

        for (int i = 0; i < Ranges.Count; i++)
        {
            dynamic min = Ranges[i].MinIndex;
            dynamic max = Ranges[i].MaxIndex;
            dynamic val = index;

            if (val >= min && val <= max) return;

            if (val == min - 1)
            {
                Ranges[i] = new RangeNode<T>(val, max);
                MergeNeighbors(i);
                return;
            }
            if (val == max + 1)
            {
                Ranges[i] = new RangeNode<T>(min, val);
                MergeNeighbors(i);
                return;
            }
        }
        
        Ranges.Add(new RangeNode<T>(index, index));
        Ranges.Sort((a, b) => a.MinIndex.CompareTo(b.MinIndex));
    }

    private void MergeNeighbors(int index)
    {
        // TODO
    }

    public void Serialize(RakBitStream bs)
    {
        // RakNet way: Compressed Count -> [Min, Max], [Min, Max]
        bs.WriteCompressed((uint)Ranges.Count);
        
        foreach (var range in Ranges)
        {
            if (range.MinIndex is uint24 uMin && range.MaxIndex is uint24 uMax)
            {
                bs.Write(uMin);
                bs.Write(uMax);
            }
            else
            {
                throw new InvalidOperationException("Type not supported in RangeList serialization yet");
            }
        }
    }

    public bool Deserialize(RakBitStream bs)
    {
        Ranges.Clear();
        if (!bs.ReadCompressed(out uint count)) return false;
        
        for (int i = 0; i < count; i++)
        {
            if (typeof(T) == typeof(uint24))
            {
                uint24 min, max;
                if (!bs.Read(out min) || !bs.Read(out max)) return false;
                
                Ranges.Add(new RangeNode<T>((T)(object)min, (T)(object)max));
            }
        }
        return true;
    }
}